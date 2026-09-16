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

    // ---- Ticket 9 (P2): ambiguous-tie reconciliation via live Stripe state ----

    [Fact]
    public async Task HandleAsync_AmbiguousTie_SubscriptionUpdated_Reconciles_From_Live_Snapshot_Not_Payload()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now), "evt_first", new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var snapshotPeriodEnd = new DateTimeOffset(Now.AddMonths(6));
        var payloadPeriodEnd = new DateTimeOffset(Now.AddMonths(1));
        var gateway = new FakeStripeGateway
        {
            // Same timestamp as the already-applied event, but a different event id — ambiguous tie.
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.updated",
                "cus_1",
                "sub_1",
                CompanyId: null,
                payloadPeriodEnd,
                CancelAtPeriodEnd: true,
                StripeStatus: "past_due",
                PriceId: null,
                EventId: "evt_second",
                EventCreatedAt: new DateTimeOffset(Now)),
        };
        gateway.SubscriptionSnapshotsById["sub_1"] = new StripeSubscriptionSnapshot(
            "sub_1", "cus_1", "active", snapshotPeriodEnd, CancelAtPeriodEnd: false, PriceId: "price_1");

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal(snapshotPeriodEnd, persisted.CurrentPeriodEnd);
        Assert.False(persisted.CancelAtPeriodEnd);
        Assert.Equal(["sub_1"], gateway.GetSubscriptionAsyncCalls);
    }

    [Fact]
    public async Task HandleAsync_AmbiguousTie_CheckoutRacingSubscriptionUpdated_Reconciles_From_Live_Snapshot()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now), "evt_updated", new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var snapshotPeriodEnd = new DateTimeOffset(Now.AddMonths(3));
        var gateway = new FakeStripeGateway
        {
            // Thin checkout payload racing an already-applied richer update at the same timestamp.
            WebhookEventToReturn = new StripeWebhookEvent(
                "checkout.session.completed",
                "cus_1",
                "sub_1",
                companyId,
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: null,
                StripeStatus: null,
                PriceId: "price_thin",
                EventId: "evt_checkout",
                EventCreatedAt: new DateTimeOffset(Now)),
        };
        gateway.SubscriptionSnapshotsById["sub_1"] = new StripeSubscriptionSnapshot(
            "sub_1", "cus_1", "active", snapshotPeriodEnd, CancelAtPeriodEnd: false, PriceId: "price_authoritative");

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal(snapshotPeriodEnd, persisted.CurrentPeriodEnd);
        Assert.False(persisted.CancelAtPeriodEnd);
        // Reconciliation for an already-activated subscription goes through UpdateFromStripe (same
        // as any ordinary customer.subscription.updated projection) — which, like every other
        // UpdateFromStripe call in this codebase, does not touch PriceId; only the initial
        // ActivateSubscription call sets it. The thin checkout payload's own PriceId is correctly
        // ignored either way (the point of this test), so PriceId is unaffected by reconciliation
        // here and remains whatever ActivateSubscription originally set.
        Assert.Equal("price_1", persisted.PriceId);
        Assert.Equal(["sub_1"], gateway.GetSubscriptionAsyncCalls);
    }

    [Fact]
    public async Task HandleAsync_AmbiguousTie_Reconciliation_Failure_Throws_And_Leaves_Event_Unprocessed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now), "evt_first", new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.updated",
                "cus_1",
                "sub_1",
                CompanyId: null,
                new DateTimeOffset(Now.AddMonths(2)),
                CancelAtPeriodEnd: true,
                StripeStatus: "past_due",
                PriceId: null,
                EventId: "evt_second",
                EventCreatedAt: new DateTimeOffset(Now)),
            GetSubscriptionAsyncException = new InvalidOperationException("Stripe API timeout"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("payload", "sig", CancellationToken.None));

        // The event was NOT recorded as processed and the projection was NOT applied — a redelivery
        // must get another chance to reconcile once the transient Stripe issue clears.
        Assert.False(await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == "evt_second"));

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal(new DateTimeOffset(Now.AddMonths(1)), persisted.CurrentPeriodEnd);
        Assert.False(persisted.CancelAtPeriodEnd);
        Assert.Equal("evt_first", persisted.LastAppliedStripeEventId);
    }

    [Fact]
    public async Task HandleAsync_AmbiguousTie_Reconciliation_Missing_Snapshot_Throws_And_Leaves_Event_Unprocessed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now), "evt_first", new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.updated",
                "cus_1",
                "sub_1",
                CompanyId: null,
                new DateTimeOffset(Now.AddMonths(2)),
                CancelAtPeriodEnd: true,
                StripeStatus: "past_due",
                PriceId: null,
                EventId: "evt_second",
                EventCreatedAt: new DateTimeOffset(Now)),
            // No SubscriptionSnapshotsById entry for "sub_1" — GetSubscriptionAsync returns null.
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("payload", "sig", CancellationToken.None));

        Assert.False(await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == "evt_second"));
        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
    }

    [Fact]
    public async Task HandleAsync_Non_Ambiguous_Event_Applies_Directly_Without_Calling_GetSubscriptionAsync()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now), "evt_first", new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var newPeriodEnd = new DateTimeOffset(Now.AddMonths(2));
        var gateway = new FakeStripeGateway
        {
            // A later, non-ambiguous timestamp — normal chronological ordering.
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.updated",
                "cus_1",
                "sub_1",
                CompanyId: null,
                newPeriodEnd,
                CancelAtPeriodEnd: true,
                StripeStatus: "past_due",
                PriceId: null,
                EventId: "evt_second",
                EventCreatedAt: new DateTimeOffset(Now.AddHours(1))),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.Equal(newPeriodEnd, persisted.CurrentPeriodEnd);
        Assert.True(persisted.CancelAtPeriodEnd);
        Assert.Empty(gateway.GetSubscriptionAsyncCalls);
    }

    [Fact]
    public async Task HandleAsync_AmbiguousTie_SubscriptionDeleted_Reconciles_To_Canceled_When_Snapshot_Is_Canceled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now), "evt_first", new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var snapshotPeriodEnd = new DateTimeOffset(Now.AddDays(5));
        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.deleted",
                "cus_1",
                "sub_1",
                CompanyId: null,
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: null,
                StripeStatus: "canceled",
                PriceId: null,
                EventId: "evt_deleted",
                EventCreatedAt: new DateTimeOffset(Now)),
        };
        gateway.SubscriptionSnapshotsById["sub_1"] = new StripeSubscriptionSnapshot(
            "sub_1", "cus_1", "canceled", snapshotPeriodEnd, CancelAtPeriodEnd: true, PriceId: "price_1");

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Canceled, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);
        Assert.Equal(snapshotPeriodEnd, persisted.CurrentPeriodEnd);
    }

    // ---- Ticket 22 / Ticket 25 (P1): fail-closed on unrecognized Stripe subscription statuses ----
    // NOTE: "paused" was the ORIGINAL example of an "unrecognized" status used by these two tests
    // before Ticket 25 explicitly mapped it to SubscriptionStatus.Paused (see MapStatus). They now
    // use a genuinely-unrecognized made-up status ("some_future_status") so they keep proving the
    // fail-closed default branch still throws for values this switch really doesn't know about —
    // without colliding with the newly-supported "paused" case exercised elsewhere in this file.

    [Fact]
    public async Task HandleAsync_SubscriptionUpdated_Unrecognized_Status_Throws_And_Leaves_Event_Unprocessed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.updated",
                "cus_123",
                "sub_456",
                CompanyId: null,
                new DateTimeOffset(Now.AddMonths(2)),
                CancelAtPeriodEnd: true,
                StripeStatus: "some_future_status",
                PriceId: null,
                EventId: "evt_unrecognized"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("payload", "sig", CancellationToken.None));
        Assert.Contains("some_future_status", ex.Message);

        Assert.False(await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == "evt_unrecognized"));

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal(new DateTimeOffset(Now.AddMonths(1)), persisted.CurrentPeriodEnd);
        Assert.False(persisted.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task HandleAsync_AmbiguousTie_Reconciliation_Unrecognized_Live_Status_Throws_And_Leaves_Event_Unprocessed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now), "evt_first", new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            // Same timestamp as the already-applied event, but a different event id — ambiguous tie —
            // forces reconciliation against the live Stripe snapshot below.
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.updated",
                "cus_1",
                "sub_1",
                CompanyId: null,
                new DateTimeOffset(Now.AddMonths(2)),
                CancelAtPeriodEnd: true,
                StripeStatus: "past_due",
                PriceId: null,
                EventId: "evt_second",
                EventCreatedAt: new DateTimeOffset(Now)),
        };
        gateway.SubscriptionSnapshotsById["sub_1"] = new StripeSubscriptionSnapshot(
            "sub_1", "cus_1", "some_future_status", new DateTimeOffset(Now.AddMonths(6)), CancelAtPeriodEnd: false, PriceId: "price_1");

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("payload", "sig", CancellationToken.None));
        Assert.Contains("some_future_status", ex.Message);

        Assert.False(await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == "evt_second"));

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal(new DateTimeOffset(Now.AddMonths(1)), persisted.CurrentPeriodEnd);
        Assert.False(persisted.CancelAtPeriodEnd);
        Assert.Equal("evt_first", persisted.LastAppliedStripeEventId);
    }

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("trialing", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("unpaid", SubscriptionStatus.PastDue)]
    [InlineData("incomplete", SubscriptionStatus.PastDue)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    [InlineData("incomplete_expired", SubscriptionStatus.Canceled)]
    public async Task HandleAsync_SubscriptionUpdated_Recognized_Status_Maps_And_Applies_Correctly(
        string stripeStatus, SubscriptionStatus expectedStatus)
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
                CancelAtPeriodEnd: false,
                StripeStatus: stripeStatus,
                PriceId: null,
                EventId: $"evt_{stripeStatus}"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(expectedStatus, persisted.Status);
        Assert.Equal(newPeriodEnd, persisted.CurrentPeriodEnd);
        Assert.True(await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == $"evt_{stripeStatus}"));
    }

    // ---- Ticket 25 (P1): "paused" / "resumed" Stripe subscription lifecycle ----

    [Fact]
    public async Task HandleAsync_SubscriptionUpdated_Paused_Status_Sets_Local_Status_To_Paused()
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
                CancelAtPeriodEnd: false,
                StripeStatus: "paused",
                PriceId: null,
                EventId: "evt_updated_paused"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Paused, persisted.Status);
        Assert.Equal(newPeriodEnd, persisted.CurrentPeriodEnd);
        Assert.True(await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == "evt_updated_paused"));
    }

    [Fact]
    public async Task HandleAsync_SubscriptionPaused_Event_Sets_Local_Status_To_Paused_Directly_From_Payload()
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
                "customer.subscription.paused",
                "cus_123",
                "sub_456",
                CompanyId: null,
                newPeriodEnd,
                CancelAtPeriodEnd: false,
                StripeStatus: "paused",
                PriceId: null,
                EventId: "evt_paused_direct"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Paused, persisted.Status);
        Assert.Equal(newPeriodEnd, persisted.CurrentPeriodEnd);
        // The gateway is never consulted for a live snapshot for a dedicated "paused" event — it is
        // always trusted directly from the payload (no access-granting risk moving TO Paused).
        Assert.Empty(gateway.GetSubscriptionAsyncCalls);
    }

    [Fact]
    public async Task HandleAsync_SubscriptionResumed_Event_Reconciles_From_Live_Snapshot_And_Restores_Active()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        subscription.UpdateFromStripe(SubscriptionStatus.Paused, new DateTimeOffset(Now.AddMonths(1)), cancelAtPeriodEnd: false, new DateTimeOffset(Now.AddDays(1)));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var snapshotPeriodEnd = new DateTimeOffset(Now.AddMonths(3));
        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.resumed",
                "cus_123",
                "sub_456",
                CompanyId: null,
                CurrentPeriodEnd: new DateTimeOffset(Now.AddMonths(1)),
                CancelAtPeriodEnd: false,
                StripeStatus: "active",
                PriceId: null,
                EventId: "evt_resumed"),
        };
        gateway.SubscriptionSnapshotsById["sub_456"] = new StripeSubscriptionSnapshot(
            "sub_456", "cus_123", "active", snapshotPeriodEnd, CancelAtPeriodEnd: false, PriceId: "price_789");

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(2)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal(snapshotPeriodEnd, persisted.CurrentPeriodEnd);
        Assert.Equal(["sub_456"], gateway.GetSubscriptionAsyncCalls);
        Assert.True(await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == "evt_resumed"));
    }

    [Fact]
    public async Task HandleAsync_SubscriptionResumed_Event_Live_Snapshot_Still_Paused_Keeps_Local_Status_Paused()
    {
        // Ticket 25 (P1): the whole point of reconciling "resumed" against live Stripe state rather
        // than trusting the payload — a racing/stale "resumed" delivery must not grant access the
        // live subscription doesn't actually have.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        subscription.UpdateFromStripe(SubscriptionStatus.Paused, new DateTimeOffset(Now.AddMonths(1)), cancelAtPeriodEnd: false, new DateTimeOffset(Now.AddDays(1)));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.resumed",
                "cus_123",
                "sub_456",
                CompanyId: null,
                CurrentPeriodEnd: new DateTimeOffset(Now.AddMonths(1)),
                CancelAtPeriodEnd: false,
                StripeStatus: "active",
                PriceId: null,
                EventId: "evt_resumed_stale"),
        };
        gateway.SubscriptionSnapshotsById["sub_456"] = new StripeSubscriptionSnapshot(
            "sub_456", "cus_123", "paused", new DateTimeOffset(Now.AddMonths(1)), CancelAtPeriodEnd: false, PriceId: "price_789");

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(2)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Paused, persisted.Status);
        Assert.Equal(["sub_456"], gateway.GetSubscriptionAsyncCalls);
    }

    [Fact]
    public async Task HandleAsync_SubscriptionResumed_Event_Reconciliation_Fetch_Exception_Throws_And_Leaves_Event_Unprocessed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        subscription.UpdateFromStripe(SubscriptionStatus.Paused, new DateTimeOffset(Now.AddMonths(1)), cancelAtPeriodEnd: false, new DateTimeOffset(Now.AddDays(1)));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.resumed",
                "cus_123",
                "sub_456",
                CompanyId: null,
                CurrentPeriodEnd: new DateTimeOffset(Now.AddMonths(1)),
                CancelAtPeriodEnd: false,
                StripeStatus: "active",
                PriceId: null,
                EventId: "evt_resumed_fetch_fail"),
            GetSubscriptionAsyncException = new InvalidOperationException("Stripe API timeout"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(2)), NullLogger<StripeWebhookHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("payload", "sig", CancellationToken.None));

        Assert.False(await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == "evt_resumed_fetch_fail"));

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Paused, persisted.Status);
    }

    [Fact]
    public async Task HandleAsync_SubscriptionResumed_Event_Missing_Live_Snapshot_Throws_And_Leaves_Event_Unprocessed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        subscription.UpdateFromStripe(SubscriptionStatus.Paused, new DateTimeOffset(Now.AddMonths(1)), cancelAtPeriodEnd: false, new DateTimeOffset(Now.AddDays(1)));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.resumed",
                "cus_123",
                "sub_456",
                CompanyId: null,
                CurrentPeriodEnd: new DateTimeOffset(Now.AddMonths(1)),
                CancelAtPeriodEnd: false,
                StripeStatus: "active",
                PriceId: null,
                EventId: "evt_resumed_no_snapshot"),
            // No SubscriptionSnapshotsById entry for "sub_456" — GetSubscriptionAsync returns null.
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(2)), NullLogger<StripeWebhookHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync("payload", "sig", CancellationToken.None));

        Assert.False(await context.ProcessedStripeEvents.AnyAsync(e => e.StripeEventId == "evt_resumed_no_snapshot"));

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Paused, persisted.Status);
    }

    [Fact]
    public async Task HandleAsync_AmbiguousTie_SubscriptionUpdated_Reconciles_To_Paused_When_Live_Snapshot_Is_Paused()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now), "evt_first", new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var snapshotPeriodEnd = new DateTimeOffset(Now.AddMonths(6));
        var gateway = new FakeStripeGateway
        {
            // Same timestamp as the already-applied event, but a different event id — ambiguous tie.
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.updated",
                "cus_1",
                "sub_1",
                CompanyId: null,
                new DateTimeOffset(Now.AddMonths(1)),
                CancelAtPeriodEnd: false,
                StripeStatus: "active",
                PriceId: null,
                EventId: "evt_second",
                EventCreatedAt: new DateTimeOffset(Now)),
        };
        gateway.SubscriptionSnapshotsById["sub_1"] = new StripeSubscriptionSnapshot(
            "sub_1", "cus_1", "paused", snapshotPeriodEnd, CancelAtPeriodEnd: false, PriceId: "price_1");

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Paused, persisted.Status);
        Assert.Equal(snapshotPeriodEnd, persisted.CurrentPeriodEnd);
        Assert.Equal(["sub_1"], gateway.GetSubscriptionAsyncCalls);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Delivery_Of_Paused_Event_Is_Idempotent_NoOp()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.paused",
                "cus_123",
                "sub_456",
                CompanyId: null,
                new DateTimeOffset(Now.AddMonths(1)),
                CancelAtPeriodEnd: false,
                StripeStatus: "paused",
                PriceId: null,
                EventId: "evt_paused_dup"),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(1)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);
        await handler.HandleAsync("payload", "sig", CancellationToken.None);
        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var rows = await context.CustomerSubscriptions.Where(s => s.CompanyId == companyId).ToListAsync();
        var persisted = Assert.Single(rows);
        Assert.Equal(SubscriptionStatus.Paused, persisted.Status);
        Assert.Equal(1, await context.ProcessedStripeEvents.CountAsync(e => e.StripeEventId == "evt_paused_dup"));
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Delivery_Of_Resumed_Event_Is_Idempotent_NoOp()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        subscription.UpdateFromStripe(SubscriptionStatus.Paused, new DateTimeOffset(Now.AddMonths(1)), cancelAtPeriodEnd: false, new DateTimeOffset(Now.AddDays(1)));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.resumed",
                "cus_123",
                "sub_456",
                CompanyId: null,
                new DateTimeOffset(Now.AddMonths(1)),
                CancelAtPeriodEnd: false,
                StripeStatus: "active",
                PriceId: null,
                EventId: "evt_resumed_dup"),
        };
        gateway.SubscriptionSnapshotsById["sub_456"] = new StripeSubscriptionSnapshot(
            "sub_456", "cus_123", "active", new DateTimeOffset(Now.AddMonths(1)), CancelAtPeriodEnd: false, PriceId: "price_789");

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(2)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);
        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var rows = await context.CustomerSubscriptions.Where(s => s.CompanyId == companyId).ToListAsync();
        var persisted = Assert.Single(rows);
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal(1, await context.ProcessedStripeEvents.CountAsync(e => e.StripeEventId == "evt_resumed_dup"));
        // GetSubscriptionAsync is only invoked on the FIRST delivery — the second is short-circuited
        // by the ProcessedStripeEvents idempotency check before reconciliation ever runs again.
        Assert.Equal(["sub_456"], gateway.GetSubscriptionAsyncCalls);
    }

    [Fact]
    public async Task HandleAsync_OutOfOrder_Older_Paused_Event_Arriving_After_Newer_Applied_Event_Is_Ignored()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        // A later event has already been applied, setting the subscription Active with a newer marker.
        subscription.UpdateFromStripe(
            SubscriptionStatus.Active, new DateTimeOffset(Now.AddMonths(1)), cancelAtPeriodEnd: false,
            new DateTimeOffset(Now.AddDays(1)), "evt_later_active", new DateTimeOffset(Now.AddHours(2)));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        // An OLDER "paused" event, delivered late.
        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.paused",
                "cus_1",
                "sub_1",
                CompanyId: null,
                new DateTimeOffset(Now.AddMonths(1)),
                CancelAtPeriodEnd: false,
                StripeStatus: "paused",
                PriceId: null,
                EventId: "evt_older_paused",
                EventCreatedAt: new DateTimeOffset(Now.AddHours(1))),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(2)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        // The stale paused event was ignored — status remains whatever the newer event set.
        Assert.Equal(SubscriptionStatus.Active, persisted.Status);
        Assert.Equal("evt_later_active", persisted.LastAppliedStripeEventId);

        // The stale event is recorded as processed (so it isn't redelivered forever) but not applied.
        var processedEvent = await context.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_older_paused");
        Assert.False(processedEvent.Applied);
    }

    [Fact]
    public async Task HandleAsync_OutOfOrder_Resumed_Event_Delivered_Before_Later_Applied_Event_Is_Ignored()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var subscription = CustomerSubscription.StartTrial(companyId, new DateTimeOffset(Now), trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", new DateTimeOffset(Now.AddMonths(1)), new DateTimeOffset(Now));
        // A later event (e.g. a subsequent "paused") has already been applied with a newer marker.
        subscription.UpdateFromStripe(
            SubscriptionStatus.Paused, new DateTimeOffset(Now.AddMonths(1)), cancelAtPeriodEnd: false,
            new DateTimeOffset(Now.AddDays(1)), "evt_later_paused", new DateTimeOffset(Now.AddHours(2)));
        context.CustomerSubscriptions.Add(subscription);
        await context.SaveChangesAsync();

        // A "resumed" event with an EARLIER timestamp than the already-applied later event, delivered
        // late (out of order) — must be ignored without reconciliation ever running, since the
        // ordering guard runs before the resumed-specific reconciliation branch.
        var gateway = new FakeStripeGateway
        {
            WebhookEventToReturn = new StripeWebhookEvent(
                "customer.subscription.resumed",
                "cus_1",
                "sub_1",
                CompanyId: null,
                new DateTimeOffset(Now.AddMonths(1)),
                CancelAtPeriodEnd: false,
                StripeStatus: "active",
                PriceId: null,
                EventId: "evt_earlier_resumed",
                EventCreatedAt: new DateTimeOffset(Now.AddHours(1))),
        };

        var handler = new StripeWebhookHandler(context, gateway, new FakeClock(Now.AddDays(2)), NullLogger<StripeWebhookHandler>.Instance);

        await handler.HandleAsync("payload", "sig", CancellationToken.None);

        var persisted = await context.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Paused, persisted.Status);
        Assert.Equal("evt_later_paused", persisted.LastAppliedStripeEventId);
        Assert.Empty(gateway.GetSubscriptionAsyncCalls);

        var processedEvent = await context.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_earlier_resumed");
        Assert.False(processedEvent.Applied);
    }

    private static CompaniesDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CompaniesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CompaniesDbContext(options);
    }
}
