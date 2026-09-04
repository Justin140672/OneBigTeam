using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.StripeWebhook;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using HR.Modules.Companies.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// OBT-REM-09: EF InMemory-level coverage for the concurrency-safe Stripe webhook handler — the
/// deterministic equal-timestamp tie-break, convergence regardless of delivery order (including an
/// update racing a delete for the same subscription), and the <see cref="CustomerSubscription.IsStaleStripeEvent"/>
/// domain rule directly. InMemory enforces the Version concurrency token (throws
/// DbUpdateConcurrencyException on a stale write), so the handler's retry loop is exercised here too,
/// but this does not exercise genuine overlapping database transactions — see
/// HR.Integration.Tests/StripeWebhookConcurrencyTests.cs for real-Postgres concurrent delivery.
/// </summary>
public class StripeWebhookConcurrencyTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);
    private readonly string _store = "stripe-conc-" + Guid.NewGuid().ToString("N");

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

    private static StripeWebhookEvent Deleted(string evtId, DateTimeOffset evtCreatedAt) =>
        new("customer.subscription.deleted", "cus_1", "sub_1", null, null, true, "canceled", null,
            EventId: evtId, EventCreatedAt: evtCreatedAt);

    // ---- Equal-EventCreatedAt tie-break (higher ordinal event id wins), both delivery orders ----

    [Fact]
    public async Task Equal_timestamp_tiebreak_higherOrdinalId_wins_when_lowerId_delivered_first()
    {
        var companyId = await SeedActiveAsync();
        var tie = new DateTimeOffset(Now.AddHours(1));

        var gw = new FakeStripeGateway { WebhookEventToReturn = Updated("active", new DateTimeOffset(Now.AddMonths(1)), "evt_aaa", tie) };
        await using (var c1 = Ctx())
            await Handler(c1, gw, Now.AddHours(2)).HandleAsync("p", "s", CancellationToken.None);

        gw.WebhookEventToReturn = Updated("past_due", new DateTimeOffset(Now.AddMonths(2)), "evt_bbb", tie, cancel: true);
        await using (var c2 = Ctx())
            await Handler(c2, gw, Now.AddHours(3)).HandleAsync("p", "s", CancellationToken.None);

        await using var verify = Ctx();
        var persisted = await verify.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        // "evt_bbb" > "evt_aaa" ordinally — the higher id wins the tie regardless of delivery order.
        // evt_aaa was legitimately applied when it arrived (no prior marker existed yet) — that
        // historical Applied=true record does not retroactively change once evt_bbb supersedes it;
        // what matters is that the FINAL effective subscription state reflects the tie-break winner.
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);
        Assert.True((await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_bbb")).Applied);
        Assert.True((await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_aaa")).Applied);
    }

    [Fact]
    public async Task Equal_timestamp_tiebreak_higherOrdinalId_wins_when_higherId_delivered_first()
    {
        var companyId = await SeedActiveAsync();
        var tie = new DateTimeOffset(Now.AddHours(1));

        var gw = new FakeStripeGateway { WebhookEventToReturn = Updated("past_due", new DateTimeOffset(Now.AddMonths(2)), "evt_bbb", tie, cancel: true) };
        await using (var c1 = Ctx())
            await Handler(c1, gw, Now.AddHours(2)).HandleAsync("p", "s", CancellationToken.None);

        gw.WebhookEventToReturn = Updated("active", new DateTimeOffset(Now.AddMonths(1)), "evt_aaa", tie);
        await using (var c2 = Ctx())
            await Handler(c2, gw, Now.AddHours(3)).HandleAsync("p", "s", CancellationToken.None);

        await using var verify = Ctx();
        var persisted = await verify.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        // Same winner ("evt_bbb") regardless of delivery order — convergence.
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);
        Assert.True((await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_bbb")).Applied);
        Assert.False((await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_aaa")).Applied);
    }

    // ---- Update vs. delete race for the same subscription — newer wins regardless of order ----

    [Fact]
    public async Task Update_then_older_delete_leaves_update_state_in_place()
    {
        var companyId = await SeedActiveAsync();

        var gw = new FakeStripeGateway
        {
            WebhookEventToReturn = Updated("past_due", new DateTimeOffset(Now.AddMonths(2)), "evt_upd", new DateTimeOffset(Now.AddHours(5))),
        };
        await using (var c1 = Ctx())
            await Handler(c1, gw, Now.AddHours(6)).HandleAsync("p", "s", CancellationToken.None);

        gw.WebhookEventToReturn = Deleted("evt_del", new DateTimeOffset(Now.AddHours(1))); // older
        await using (var c2 = Ctx())
            await Handler(c2, gw, Now.AddHours(7)).HandleAsync("p", "s", CancellationToken.None);

        await using var verify = Ctx();
        var persisted = await verify.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status); // delete lost, did not overwrite
        Assert.False((await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_del")).Applied);
    }

    [Fact]
    public async Task Older_update_then_newer_delete_ends_canceled()
    {
        var companyId = await SeedActiveAsync();

        var gw = new FakeStripeGateway
        {
            WebhookEventToReturn = Updated("past_due", new DateTimeOffset(Now.AddMonths(2)), "evt_upd", new DateTimeOffset(Now.AddHours(1))),
        };
        await using (var c1 = Ctx())
            await Handler(c1, gw, Now.AddHours(2)).HandleAsync("p", "s", CancellationToken.None);

        gw.WebhookEventToReturn = Deleted("evt_del", new DateTimeOffset(Now.AddHours(5))); // newer
        await using (var c2 = Ctx())
            await Handler(c2, gw, Now.AddHours(6)).HandleAsync("p", "s", CancellationToken.None);

        await using var verify = Ctx();
        var persisted = await verify.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Canceled, persisted.Status);
        Assert.True((await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_del")).Applied);
    }

    [Fact]
    public async Task Delete_then_older_update_leaves_canceled_state_in_place()
    {
        var companyId = await SeedActiveAsync();

        var gw = new FakeStripeGateway { WebhookEventToReturn = Deleted("evt_del", new DateTimeOffset(Now.AddHours(5))) };
        await using (var c1 = Ctx())
            await Handler(c1, gw, Now.AddHours(6)).HandleAsync("p", "s", CancellationToken.None);

        gw.WebhookEventToReturn = Updated("active", new DateTimeOffset(Now.AddMonths(1)), "evt_upd", new DateTimeOffset(Now.AddHours(1))); // older
        await using (var c2 = Ctx())
            await Handler(c2, gw, Now.AddHours(7)).HandleAsync("p", "s", CancellationToken.None);

        await using var verify = Ctx();
        var persisted = await verify.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
        Assert.Equal(SubscriptionStatus.Canceled, persisted.Status);
        Assert.False((await verify.ProcessedStripeEvents.SingleAsync(e => e.StripeEventId == "evt_upd")).Applied);
    }

    // ---- CustomerSubscription.IsStaleStripeEvent — direct domain-level coverage ----

    [Fact]
    public void IsStaleStripeEvent_returns_false_when_no_marker_yet()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), new DateTimeOffset(Now), 14);

        Assert.False(subscription.IsStaleStripeEvent("evt_1", new DateTimeOffset(Now)));
        Assert.False(subscription.IsStaleStripeEvent(null, null));
    }

    [Fact]
    public void IsStaleStripeEvent_returns_false_when_incoming_eventCreatedAt_is_null()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), new DateTimeOffset(Now), 14);
        subscription.ActivateSubscription("cus", "sub", "price", null, new DateTimeOffset(Now), "evt_applied", new DateTimeOffset(Now));

        Assert.False(subscription.IsStaleStripeEvent("evt_new", null));
    }

    [Fact]
    public void IsStaleStripeEvent_returns_true_for_strictly_older_timestamp()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), new DateTimeOffset(Now), 14);
        subscription.ActivateSubscription("cus", "sub", "price", null, new DateTimeOffset(Now), "evt_applied", new DateTimeOffset(Now.AddHours(5)));

        Assert.True(subscription.IsStaleStripeEvent("evt_older", new DateTimeOffset(Now.AddHours(4))));
    }

    [Fact]
    public void IsStaleStripeEvent_returns_false_for_strictly_newer_timestamp()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), new DateTimeOffset(Now), 14);
        subscription.ActivateSubscription("cus", "sub", "price", null, new DateTimeOffset(Now), "evt_applied", new DateTimeOffset(Now.AddHours(5)));

        Assert.False(subscription.IsStaleStripeEvent("evt_newer", new DateTimeOffset(Now.AddHours(6))));
    }

    [Fact]
    public void IsStaleStripeEvent_equal_timestamp_lowerOrdinalId_is_stale()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), new DateTimeOffset(Now), 14);
        var tie = new DateTimeOffset(Now.AddHours(5));
        subscription.ActivateSubscription("cus", "sub", "price", null, new DateTimeOffset(Now), "evt_bbb", tie);

        Assert.True(subscription.IsStaleStripeEvent("evt_aaa", tie));
    }

    [Fact]
    public void IsStaleStripeEvent_equal_timestamp_equalId_is_stale()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), new DateTimeOffset(Now), 14);
        var tie = new DateTimeOffset(Now.AddHours(5));
        subscription.ActivateSubscription("cus", "sub", "price", null, new DateTimeOffset(Now), "evt_bbb", tie);

        // Redelivery of the exact same event id/timestamp — the "<=" comparison treats it as stale
        // (the idempotency check in the handler would normally short-circuit before this is reached).
        Assert.True(subscription.IsStaleStripeEvent("evt_bbb", tie));
    }

    [Fact]
    public void IsStaleStripeEvent_equal_timestamp_higherOrdinalId_is_not_stale()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), new DateTimeOffset(Now), 14);
        var tie = new DateTimeOffset(Now.AddHours(5));
        subscription.ActivateSubscription("cus", "sub", "price", null, new DateTimeOffset(Now), "evt_bbb", tie);

        Assert.False(subscription.IsStaleStripeEvent("evt_ccc", tie));
    }
}
