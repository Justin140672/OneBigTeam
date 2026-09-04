using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.StripeWebhook;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// OBT-REM-07: the Stripe webhook handler must be idempotent on Stripe's own event id and must not
/// let an out-of-order (older) subscription lifecycle event overwrite newer state. Each processed
/// event is recorded in <c>processed_stripe_events</c> with its creation/processing timestamps.
/// </summary>
public class StripeWebhookIdempotencyOrderingTests
{
    private static readonly DateTime Now = new(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
    private readonly string _store = "stripe-idem-" + Guid.NewGuid().ToString("N");

    private CompaniesDbContext Ctx() => new(
        new DbContextOptionsBuilder<CompaniesDbContext>().UseInMemoryDatabase(_store).Options);

    private static StripeWebhookHandler Handler(CompaniesDbContext ctx, FakeStripeGateway gw, DateTime now) =>
        new(ctx, gw, new FakeClock(now), NullLogger<StripeWebhookHandler>.Instance);

    private async Task<Guid> SeedActiveAsync(string cus = "cus_1", string sub = "sub_1")
    {
        await using var ctx = Ctx();
        var companyId = Guid.NewGuid();
        var s = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14);
        s.ActivateSubscription(cus, sub, "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        ctx.CustomerSubscriptions.Add(s);
        await ctx.SaveChangesAsync();
        return companyId;
    }

    private static StripeWebhookEvent Updated(
        string status, DateTimeOffset periodEnd, string evtId, DateTimeOffset evtCreatedAt, bool cancel = false) =>
        new("customer.subscription.updated", "cus_1", "sub_1", null, periodEnd, cancel, status, null,
            EventId: evtId, EventCreatedAt: evtCreatedAt);

    [Fact]
    public async Task Identical_event_delivered_twice_applies_once_and_records_one_row()
    {
        var companyId = await SeedActiveAsync();
        var periodEnd = new DateTimeOffset(Now.AddMonths(2));
        var evt = Updated("past_due", periodEnd, "evt_dup", new DateTimeOffset(Now.AddHours(1)));

        var gw = new FakeStripeGateway { WebhookEventToReturn = evt };

        await using (var c1 = Ctx())
            await Handler(c1, gw, Now.AddHours(2)).HandleAsync("p", "s", CancellationToken.None);

        DateTimeOffset updatedAtAfterFirst;
        await using (var c = Ctx())
            updatedAtAfterFirst = (await c.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId)).UpdatedAt;

        // Redelivery at a later wall-clock time.
        await using (var c2 = Ctx())
            await Handler(c2, gw, Now.AddHours(9)).HandleAsync("p", "s", CancellationToken.None);

        await using var verify = Ctx();
        var persisted = await verify.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.Equal(updatedAtAfterFirst, persisted.UpdatedAt); // 2nd delivery did not re-apply

        var rows = await verify.ProcessedStripeEvents.Where(e => e.StripeEventId == "evt_dup").ToListAsync();
        Assert.Single(rows);
        Assert.True(rows[0].Applied);
        Assert.Equal(new DateTimeOffset(Now.AddHours(1)), rows[0].EventCreatedAt);
        Assert.Equal(new DateTimeOffset(Now.AddHours(2)), rows[0].ProcessedAt);
    }

    [Fact]
    public async Task Older_update_after_newer_is_ignored_and_recorded_as_not_applied()
    {
        var companyId = await SeedActiveAsync();
        var newerEnd = new DateTimeOffset(Now.AddMonths(3));
        var olderEnd = new DateTimeOffset(Now.AddMonths(1));

        var gw = new FakeStripeGateway
        {
            WebhookEventToReturn = Updated("active", newerEnd, "evt_newer", new DateTimeOffset(Now.AddHours(5))),
        };
        await using (var c1 = Ctx())
            await Handler(c1, gw, Now.AddHours(6)).HandleAsync("p", "s", CancellationToken.None);

        gw.WebhookEventToReturn = Updated("past_due", olderEnd, "evt_older", new DateTimeOffset(Now.AddHours(1)), cancel: true);
        await using (var c2 = Ctx())
            await Handler(c2, gw, Now.AddHours(7)).HandleAsync("p", "s", CancellationToken.None);

        await using var verify = Ctx();
        var persisted = await verify.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal(newerEnd, persisted.CurrentPeriodEnd);
        Assert.False(persisted.CancelAtPeriodEnd);

        var older = await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_older");
        Assert.False(older.Applied);
        var newer = await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_newer");
        Assert.True(newer.Applied);
    }

    [Fact]
    public async Task Newer_update_after_older_is_applied()
    {
        var companyId = await SeedActiveAsync();

        var gw = new FakeStripeGateway
        {
            WebhookEventToReturn = Updated("active", new DateTimeOffset(Now.AddMonths(1)), "evt_1", new DateTimeOffset(Now.AddHours(1))),
        };
        await using (var c1 = Ctx())
            await Handler(c1, gw, Now.AddHours(2)).HandleAsync("p", "s", CancellationToken.None);

        gw.WebhookEventToReturn = Updated("past_due", new DateTimeOffset(Now.AddMonths(2)), "evt_2", new DateTimeOffset(Now.AddHours(3)), cancel: true);
        await using (var c2 = Ctx())
            await Handler(c2, gw, Now.AddHours(4)).HandleAsync("p", "s", CancellationToken.None);

        await using var verify = Ctx();
        var persisted = await verify.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);
        Assert.True((await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_2")).Applied);
    }

    [Fact]
    public async Task Checkout_updated_deleted_transitions_each_recorded_applied_with_timestamps()
    {
        await using (var ctx = Ctx())
        {
            var companyId = Guid.NewGuid();
            ctx.CustomerSubscriptions.Add(CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), 14));
            await ctx.SaveChangesAsync();

            var gw = new FakeStripeGateway
            {
                WebhookEventToReturn = new StripeWebhookEvent(
                    "checkout.session.completed", "cus_1", "sub_1", companyId, null, null, null, "price_1",
                    EventId: "evt_checkout", EventCreatedAt: new DateTimeOffset(Now.AddMinutes(1))),
            };
            await using (var c = Ctx())
                await Handler(c, gw, Now.AddMinutes(2)).HandleAsync("p", "s", CancellationToken.None);

            gw.WebhookEventToReturn = Updated("active", new DateTimeOffset(Now.AddMonths(2)), "evt_upd", new DateTimeOffset(Now.AddMinutes(3)));
            await using (var c = Ctx())
                await Handler(c, gw, Now.AddMinutes(4)).HandleAsync("p", "s", CancellationToken.None);

            gw.WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.deleted", "cus_1", "sub_1", null, null, null, "canceled", null,
                EventId: "evt_del", EventCreatedAt: new DateTimeOffset(Now.AddMinutes(5)));
            await using (var c = Ctx())
                await Handler(c, gw, Now.AddMinutes(6)).HandleAsync("p", "s", CancellationToken.None);
        }

        await using var verify = Ctx();
        var all = await verify.ProcessedStripeEvents.OrderBy(e => e.EventCreatedAt).ToListAsync();
        Assert.Equal(new[] { "evt_checkout", "evt_upd", "evt_del" }, all.Select(e => e.StripeEventId).ToArray());
        Assert.All(all, e => Assert.True(e.Applied));
        Assert.All(all, e => Assert.NotEqual(default, e.ProcessedAt));
        Assert.All(all, e => Assert.Equal("sub_1", e.StripeSubscriptionId));
    }

    [Fact]
    public async Task Invalid_signature_writes_no_processed_row_and_changes_nothing()
    {
        var companyId = await SeedActiveAsync();
        var gw = new FakeStripeGateway
        {
            ExceptionToThrowOnConstructEvent = new InvalidOperationException("Invalid Stripe signature."),
        };

        await using (var c = Ctx())
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Handler(c, gw, Now).HandleAsync("p", "bad", CancellationToken.None));

        await using var verify = Ctx();
        Assert.False(await verify.ProcessedStripeEvents.AnyAsync());
        Assert.Equal(SubscriptionStatus.Active,
            (await verify.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId)).Status);
    }

    [Fact]
    public async Task Unknown_event_type_writes_no_processed_row()
    {
        var companyId = await SeedActiveAsync();
        var gw = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "invoice.updated", "cus_1", "sub_1", companyId, null, null, "active", null,
                EventId: "evt_unknown", EventCreatedAt: new DateTimeOffset(Now.AddHours(1))),
        };

        await using (var c = Ctx())
            await Handler(c, gw, Now.AddHours(2)).HandleAsync("p", "s", CancellationToken.None);

        await using var verify = Ctx();
        Assert.False(await verify.ProcessedStripeEvents.AnyAsync());
    }

    [Fact]
    public async Task Gateway_failure_before_persistence_writes_no_processed_row()
    {
        await SeedActiveAsync();
        var gw = new FakeStripeGateway { ExceptionToThrowOnConstructEvent = new TimeoutException("timed out") };

        await using (var c = Ctx())
            await Assert.ThrowsAsync<TimeoutException>(
                () => Handler(c, gw, Now).HandleAsync("p", "s", CancellationToken.None));

        await using var verify = Ctx();
        Assert.False(await verify.ProcessedStripeEvents.AnyAsync());
    }
}
