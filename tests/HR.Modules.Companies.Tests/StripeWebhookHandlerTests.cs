using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.StripeWebhook;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Companies.Tests;

public class StripeWebhookHandlerTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_CheckoutSessionCompleted_Activates_Matched_Subscription()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var currentPeriodEnd = new DateTimeOffset(Now.AddMonths(1));
        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "checkout.session.completed",
                "cus_123",
                "sub_456",
                companyId,
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: null,
                StripeStatus: null,
                PriceId: "price_789"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal("cus_123", persisted.StripeCustomerId);
        Assert.Equal("sub_456", persisted.StripeSubscriptionId);
        Assert.Equal("price_789", persisted.PriceId);
        Assert.False(persisted.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task HandleAsync_SubscriptionUpdated_Updates_Matched_Subscription_By_StripeCustomerId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var newPeriodEnd = new DateTimeOffset(Now.AddMonths(2));
        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.updated",
                "cus_123",
                "sub_456",
                CompanyId: null,
                newPeriodEnd,
                CancelAtPeriodEnd: true,
                StripeStatus: "past_due",
                PriceId: null),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.Equal(newPeriodEnd, persisted.CurrentPeriodEnd);
        Assert.True(persisted.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task HandleAsync_SubscriptionDeleted_Cancels_Matched_Subscription_By_StripeSubscriptionId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_999", "sub_999", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            // No StripeCustomerId on this event — must fall back to matching by StripeSubscriptionId.
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.deleted",
                StripeCustomerId: null,
                "sub_999",
                CompanyId: null,
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: null,
                StripeStatus: "canceled",
                PriceId: null),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(2)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Canceled, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_CompanyId_When_No_Stripe_Ids_Match()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "checkout.session.completed",
                "cus_unmatched",
                "sub_unmatched",
                companyId,
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: null,
                StripeStatus: null,
                PriceId: "price_1"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal("cus_unmatched", persisted.StripeCustomerId);
    }

    [Fact]
    public async Task HandleAsync_Unrecognised_EventType_Does_Not_Modify_Subscription()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "invoice.payment_failed",
                null,
                null,
                companyId,
                null,
                null,
                null,
                null),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Trial, persisted.Status);
    }

    [Fact]
    public async Task HandleAsync_Unmatched_Subscription_Does_Not_Throw()
    {
        await using var context = BuildContext();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "checkout.session.completed",
                "cus_none",
                "sub_none",
                CompanyId: null,
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: null,
                StripeStatus: null,
                PriceId: "price_1"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        Assert.False(await context.CustomerSubscriptions.AnyAsync());
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
