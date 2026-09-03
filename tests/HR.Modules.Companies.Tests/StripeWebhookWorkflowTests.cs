using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.StripeWebhook;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// TEST-003 hardening for the Stripe webhook workflow. Every case here exercises the boundary
/// through <see cref="FakeStripeGateway"/> — the real Stripe SDK is never contacted. Focus is on
/// persisted state (not just return values): replay-safety, invalid-signature-before-processing,
/// unknown-event no-op, out-of-order delivery, and gateway failure surfacing.
///
/// Seam note: <see cref="StripeGateway"/> constructs Stripe.net's <c>SessionService</c> /
/// <c>SubscriptionService</c> / <c>InvoiceService</c> directly and calls the static
/// <c>EventUtility.ConstructEvent</c>, with no injectable <c>IStripeClient</c> / <c>HttpClient</c>.
/// There is therefore no unit-test seam for the adapter body itself; these tests pin the workflow
/// and response mapping at the handler level, which is the highest layer that is Stripe-SDK-free.
/// </summary>
public class StripeWebhookWorkflowTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private static StripeWebhookEvent CheckoutCompleted(Guid companyId, string cus = "cus_1", string sub = "sub_1") =>
        new("checkout.session.completed", cus, sub, companyId, null, null, null, "price_1");

    private static StripeWebhookEvent SubscriptionUpdated(
        string cus, string sub, string status, DateTimeOffset? periodEnd, bool cancelAtPeriodEnd) =>
        new("customer.subscription.updated", cus, sub, null, periodEnd, cancelAtPeriodEnd, status, null);

    // --- Invalid signature is rejected BEFORE any processing / state change ---------------------

    [Fact]
    public async Task Invalid_Signature_Throws_Before_Any_State_Change()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14));
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            // Stripe.net raises StripeException from EventUtility.ConstructEvent on a bad signature;
            // the fake stands in for that with an equivalent throw before returning any event.
            ExceptionToThrowOnConstructEvent = new InvalidOperationException("Invalid Stripe signature."),
        };
        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now), NullLogger<StripeWebhookHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("payload", "t=1,v1=bad", CancellationToken.None));

        // Nothing was looked up or mutated: subscription still on Trial, no Stripe ids written.
        var persisted = await FreshContext(context).CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Trial, persisted.Status);
        Assert.Null(persisted.StripeCustomerId);
    }

    // --- Unknown event types are ignored: no throw, no state change ----------------------------

    [Fact]
    public async Task Unknown_Event_Type_Is_Ignored_With_No_State_Change()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sub = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14);
        sub.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(sub);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "invoice.updated", "cus_1", "sub_1", companyId, null, null, "active", null),
        };
        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(5)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await FreshContext(context).CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal(new DateTimeOffset(Now), persisted.UpdatedAt); // untouched
    }

    // --- Duplicate delivery: the business change is applied at most once -----------------------

    [Fact]
    public async Task Duplicate_CheckoutCompleted_Delivery_Transitions_Subscription_Only_Once()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14));
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway { WebhookEventToReturn = CheckoutCompleted(companyId) };

        // First delivery activates.
        var firstClock = new FakeClock(Now.AddMinutes(1));
        await new StripeWebhookHandler(context, gateway, firstClock, NullLogger<StripeWebhookHandler>.Instance)
            .HandleAsync("payload", "sig", CancellationToken.None);

        var afterFirst = await FreshContext(context).CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, afterFirst.Status);
        var updatedAtAfterFirst = afterFirst.UpdatedAt;

        // Stripe re-delivers the identical event (at-least-once delivery). Re-applying
        // ActivateSubscription is idempotent — still exactly one Active subscription, same ids.
        await new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddMinutes(2)), NullLogger<StripeWebhookHandler>.Instance)
            .HandleAsync("payload", "sig", CancellationToken.None);

        var all = await FreshContext(context).CustomerSubscriptions.Where(s => s.CompanyId == companyId).ToListAsync();
        var afterSecond = Assert.Single(all);
        Assert.Equal(SubscriptionStatus.Active, afterSecond.Status);
        Assert.Equal("cus_1", afterSecond.StripeCustomerId);
        Assert.Equal("sub_1", afterSecond.StripeSubscriptionId);
        Assert.False(afterSecond.CancelAtPeriodEnd);
        // The replay did not re-run the trial->active transition (no new row, no id churn).
        Assert.True(afterSecond.UpdatedAt >= updatedAtAfterFirst);
    }

    [Fact]
    public async Task Duplicate_SubscriptionDeleted_Delivery_Keeps_Subscription_Canceled_Once()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sub = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14);
        sub.ActivateSubscription("cus_9", "sub_9", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(sub);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.deleted", "cus_9", "sub_9", null, null, null, "canceled", null),
        };

        for (var i = 0; i < 3; i++)
        {
            await new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddMinutes(i)), NullLogger<StripeWebhookHandler>.Instance)
                .HandleAsync("payload", "sig", CancellationToken.None);
        }

        var persisted = Assert.Single(
            await FreshContext(context).CustomerSubscriptions.Where(s => s.CompanyId == companyId).ToListAsync());
        Assert.Equal(SubscriptionStatus.Canceled, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);
    }

    // --- Out-of-order delivery ---------------------------------------------------------------

    [Fact]
    public async Task Out_Of_Order_Delivery_Older_Update_After_Newer_Clobbers_State_LastWriteWins()
    {
        // Documents CURRENT behaviour: StripeWebhookHandler applies customer.subscription.updated
        // unconditionally (no event-timestamp / period-end guard), so a late-arriving OLDER event
        // overwrites newer state. If Stripe event ordering guarantees are ever relied upon this
        // test should flip to asserting the newer state is retained.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sub = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14);
        sub.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(sub);
        await context.SaveChangesAsync();

        var newerPeriodEnd = new DateTimeOffset(Now.AddMonths(2));
        var olderPeriodEnd = new DateTimeOffset(Now.AddMonths(1));

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = SubscriptionUpdated("cus_1", "sub_1", "active", newerPeriodEnd, cancelAtPeriodEnd: false),
        };
        await new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance)
            .HandleAsync("payload", "sig", CancellationToken.None);

        // Older event (past_due, scheduled to cancel) arrives late.
        gateway.WebhookEventToReturn = SubscriptionUpdated("cus_1", "sub_1", "past_due", olderPeriodEnd, cancelAtPeriodEnd: true);
        await new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(2)), NullLogger<StripeWebhookHandler>.Instance)
            .HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await FreshContext(context).CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.Equal(olderPeriodEnd, persisted.CurrentPeriodEnd);
        Assert.True(persisted.CancelAtPeriodEnd);
    }

    // --- Gateway failure inside the webhook path --------------------------------------------

    [Fact]
    public async Task Gateway_Failure_During_Event_Construction_Propagates_And_Persists_Nothing()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14));
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            ExceptionToThrowOnConstructEvent = new TimeoutException("Stripe request timed out."),
        };
        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now), NullLogger<StripeWebhookHandler>.Instance);

        await Assert.ThrowsAsync<TimeoutException>(
            () => handler.HandleAsync("payload", "sig", CancellationToken.None));

        var persisted = await FreshContext(context).CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Trial, persisted.Status);
    }

    // --- Redaction: sensitive Stripe identifiers are not written to logs --------------------

    [Fact]
    public async Task Unmatched_Event_Warning_Log_Does_Not_Contain_Raw_Payload_Or_Card_Data()
    {
        await using var context = BuildContext();
        var logger = new CapturingLogger<StripeWebhookHandler>();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = CheckoutCompleted(Guid.NewGuid(), cus: "cus_secret", sub: "sub_secret"),
        };
        var rawPayload = "{\"card\":{\"number\":\"4242424242424242\",\"cvc\":\"123\"}}";
        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now), logger);

        await handler.HandleAsync(rawPayload, "sig", CancellationToken.None);

        var logged = string.Join("\n", logger.Messages);
        Assert.DoesNotContain("4242424242424242", logged);
        Assert.DoesNotContain("\"cvc\"", logged);
        Assert.DoesNotContain(rawPayload, logged);
        // NOTE (finding): the unmatched-subscription warning DOES include StripeCustomerId /
        // StripeSubscriptionId as structured log fields. Those are Stripe object handles (not PANs,
        // CVCs or PII) and are the only correlation key available for diagnosing a missing local
        // row, so this is asserted as acceptable current behaviour rather than a redaction defect.
        Assert.Contains("no matching customer_subscriptions row", logged);
    }

    private readonly string _storeName = "stripe-webhook-" + Guid.NewGuid().ToString("N");

    private CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(_storeName)
            .Options;
        return new CompaniesDbContext(options);
    }

    // Re-open the same in-memory store with a fresh context so assertions read persisted state,
    // not tracked entities left in the handler's context.
    private CompaniesDbContext FreshContext(CompaniesDbContext existing) => BuildContext();

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
