using System.Net;
using System.Net.Http.Headers;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// OBT-REM-09: genuine-concurrency coverage for the Stripe webhook handler's optimistic-concurrency
/// retry loop, run against the real Postgres-backed <see cref="ApiWebApplicationFactory"/> (not EF
/// InMemory — see HR.Modules.Companies.Tests/StripeWebhookConcurrencyTests.cs for the InMemory-level
/// coverage of the same rules). Two HttpClient requests are fired via Task.WhenAll so the handler's
/// two invocations genuinely race each other's SaveChangesAsync calls against the same row.
/// </summary>
[Collection("Integration")]
public class StripeWebhookConcurrencyTests
{
    private readonly ApiWebApplicationFactory _factory;

    public StripeWebhookConcurrencyTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.StripeGateway.Reset();
    }

    private async Task<Guid> SeedCompanyWithSubscriptionAsync(string stripeCustomerId, string stripeSubscriptionId)
    {
        var companyId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        db.Companies.Add(Company.Create(companyId, $"Test Company {companyId:N}", DateTimeOffset.UtcNow));
        var subscription = CustomerSubscription.StartTrial(companyId, DateTimeOffset.UtcNow, trialLengthDays: 14);
        subscription.ActivateSubscription(stripeCustomerId, stripeSubscriptionId, "price_1", DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return companyId;
    }

    private static HttpRequestMessage BuildRequest(string payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/companies/stripe-webhook")
        {
            Content = new StringContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        if (signature is not null)
        {
            request.Headers.Add("Stripe-Signature", signature);
        }

        return request;
    }

    private async Task<CustomerSubscription> LoadAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        return await db.CustomerSubscriptions.SingleAsync(s => s.CompanyId == companyId);
    }

    private async Task<List<ProcessedStripeEvent>> LoadProcessedAsync(params string[] eventIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        return await db.ProcessedStripeEvents.Where(e => eventIds.Contains(e.StripeEventId)).ToListAsync();
    }

    [Fact]
    public async Task Concurrent_older_and_newer_update_events_converge_on_the_newer_state()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_race_1", "sub_race_1");
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero); // microsecond-truncated for Postgres round-trip

        var olderEvent = new StripeWebhookEvent(
            "customer.subscription.updated", "cus_race_1", "sub_race_1", null,
            CurrentPeriodEnd: now.AddMonths(1), CancelAtPeriodEnd: false, StripeStatus: "active", PriceId: null,
            EventId: "evt_race_older", EventCreatedAt: now.AddHours(1));

        var newerEvent = new StripeWebhookEvent(
            "customer.subscription.updated", "cus_race_1", "sub_race_1", null,
            CurrentPeriodEnd: now.AddMonths(3), CancelAtPeriodEnd: true, StripeStatus: "past_due", PriceId: null,
            EventId: "evt_race_newer", EventCreatedAt: now.AddHours(5));

        _factory.StripeGateway.WebhookEventsByPayload["older-payload"] = olderEvent;
        _factory.StripeGateway.WebhookEventsByPayload["newer-payload"] = newerEvent;

        using var client = _factory.CreateClient();
        var olderTask = client.SendAsync(BuildRequest("older-payload", "t=1,v1=fake"));
        var newerTask = client.SendAsync(BuildRequest("newer-payload", "t=1,v1=fake"));
        var responses = await Task.WhenAll(olderTask, newerTask);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var persisted = await LoadAsync(companyId);
        // The FINAL persisted state deterministically reflects the chronologically newer event
        // regardless of which physical write happens to land at the database first — that is the
        // property this ticket requires. Per-event Applied flags are not asserted here: whichever
        // event's write physically commits FIRST legitimately gets Applied=true at that moment, and
        // that historical record is not retroactively flipped when a later event supersedes it — see
        // the InMemory-level test for deterministic-ordering coverage of the Applied flags themselves.
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.Equal(now.AddMonths(3), persisted.CurrentPeriodEnd);
        Assert.True(persisted.CancelAtPeriodEnd);

        var processed = await LoadProcessedAsync("evt_race_older", "evt_race_newer");
        Assert.Equal(2, processed.Count);
    }

    [Fact]
    public async Task Concurrent_update_and_delete_events_converge_on_the_chronologically_newer_one()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_race_2", "sub_race_2");
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero); // microsecond-truncated for Postgres round-trip

        // The delete is chronologically NEWER than the update, even though it is fired as the
        // "first" of the two tasks below — the assertion must not depend on HTTP pipeline ordering.
        var updateEvent = new StripeWebhookEvent(
            "customer.subscription.updated", "cus_race_2", "sub_race_2", null,
            CurrentPeriodEnd: now.AddMonths(2), CancelAtPeriodEnd: false, StripeStatus: "active", PriceId: null,
            EventId: "evt_race_update", EventCreatedAt: now.AddHours(1));

        var deleteEvent = new StripeWebhookEvent(
            "customer.subscription.deleted", "cus_race_2", "sub_race_2", null,
            CurrentPeriodEnd: now.AddMonths(2), CancelAtPeriodEnd: true, StripeStatus: "canceled", PriceId: null,
            EventId: "evt_race_delete", EventCreatedAt: now.AddHours(5));

        _factory.StripeGateway.WebhookEventsByPayload["delete-payload"] = deleteEvent;
        _factory.StripeGateway.WebhookEventsByPayload["update-payload"] = updateEvent;

        using var client = _factory.CreateClient();
        var deleteTask = client.SendAsync(BuildRequest("delete-payload", "t=1,v1=fake"));
        var updateTask = client.SendAsync(BuildRequest("update-payload", "t=1,v1=fake"));
        var responses = await Task.WhenAll(deleteTask, updateTask);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var persisted = await LoadAsync(companyId);
        // As above: the final state deterministically reflects the chronologically newer (delete)
        // event regardless of physical commit order; per-event Applied flags are not asserted.
        Assert.Equal(SubscriptionStatus.Canceled, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);

        var processed = await LoadProcessedAsync("evt_race_update", "evt_race_delete");
        Assert.Equal(2, processed.Count);
    }

    [Fact]
    public async Task Concurrent_delivery_of_the_same_event_id_applies_exactly_once()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_race_3", "sub_race_3");
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero); // microsecond-truncated for Postgres round-trip

        _factory.StripeGateway.WebhookEventToReturn = new StripeWebhookEvent(
            "customer.subscription.updated", "cus_race_3", "sub_race_3", null,
            CurrentPeriodEnd: now.AddMonths(2), CancelAtPeriodEnd: true, StripeStatus: "past_due", PriceId: null,
            EventId: "evt_race_dup", EventCreatedAt: now.AddHours(1));

        using var client = _factory.CreateClient();
        var request1 = client.SendAsync(BuildRequest("dup-payload", "t=1,v1=fake"));
        var request2 = client.SendAsync(BuildRequest("dup-payload", "t=1,v1=fake"));
        var responses = await Task.WhenAll(request1, request2);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var persisted = await LoadAsync(companyId);
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        // StartTrial=1, seed ActivateSubscription=2, one applied concurrent-duplicate event -> 3.
        // If the event had been applied twice this would be 4.
        Assert.Equal(3, persisted.Version);

        var processed = await LoadProcessedAsync("evt_race_dup");
        Assert.Single(processed);
        Assert.True(processed[0].Applied);
    }

    [Fact]
    public async Task Concurrent_equal_timestamp_events_converge_on_the_deterministic_tiebreak_winner()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_race_4", "sub_race_4");
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero); // microsecond-truncated for Postgres round-trip
        var tie = now.AddHours(3);

        var lowerIdEvent = new StripeWebhookEvent(
            "customer.subscription.updated", "cus_race_4", "sub_race_4", null,
            CurrentPeriodEnd: now.AddMonths(1), CancelAtPeriodEnd: false, StripeStatus: "active", PriceId: null,
            EventId: "evt_aaa_tie", EventCreatedAt: tie);

        var higherIdEvent = new StripeWebhookEvent(
            "customer.subscription.updated", "cus_race_4", "sub_race_4", null,
            CurrentPeriodEnd: now.AddMonths(4), CancelAtPeriodEnd: true, StripeStatus: "past_due", PriceId: null,
            EventId: "evt_zzz_tie", EventCreatedAt: tie);

        _factory.StripeGateway.WebhookEventsByPayload["lower-payload"] = lowerIdEvent;
        _factory.StripeGateway.WebhookEventsByPayload["higher-payload"] = higherIdEvent;

        using var client = _factory.CreateClient();
        var lowerTask = client.SendAsync(BuildRequest("lower-payload", "t=1,v1=fake"));
        var higherTask = client.SendAsync(BuildRequest("higher-payload", "t=1,v1=fake"));
        var responses = await Task.WhenAll(lowerTask, higherTask);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var persisted = await LoadAsync(companyId);
        // "evt_zzz_tie" > "evt_aaa_tie" ordinally — the FINAL persisted state deterministically
        // reflects the higher-id winner regardless of which physical write lands at the database
        // first. (Per-event Applied flags are NOT asserted here: whichever event happens to commit
        // first legitimately gets Applied=true at that moment — that historical record does not
        // retroactively flip even if a later event supersedes it. Only the tie-break's effect on the
        // FINAL subscription state is deterministic; see the InMemory-level test for a
        // deterministic-ordering check of the Applied flags themselves.)
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);

        // Both events are recorded exactly once each (idempotency preserved under real concurrency);
        // which one carries Applied=true depends on physical commit order and is not asserted here —
        // what's deterministic is the FINAL subscription state above.
        var processed = await LoadProcessedAsync("evt_aaa_tie", "evt_zzz_tie");
        Assert.Equal(2, processed.Count);
    }

    [Fact]
    public async Task Replaying_the_losing_event_after_a_concurrency_conflict_stays_a_noop()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_race_5", "sub_race_5");
        // Truncated to microsecond precision (PostgreSQL's timestamptz resolution) so the round-tripped
        // persisted value compares equal to the in-memory value below — .NET DateTimeOffset carries
        // 100ns ticks, one digit finer than Postgres stores.
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);

        var olderEvent = new StripeWebhookEvent(
            "customer.subscription.updated", "cus_race_5", "sub_race_5", null,
            CurrentPeriodEnd: now.AddMonths(1), CancelAtPeriodEnd: false, StripeStatus: "active", PriceId: null,
            EventId: "evt_replay_older", EventCreatedAt: now.AddHours(1));

        var newerEvent = new StripeWebhookEvent(
            "customer.subscription.updated", "cus_race_5", "sub_race_5", null,
            CurrentPeriodEnd: now.AddMonths(3), CancelAtPeriodEnd: true, StripeStatus: "past_due", PriceId: null,
            EventId: "evt_replay_newer", EventCreatedAt: now.AddHours(5));

        _factory.StripeGateway.WebhookEventsByPayload["replay-older"] = olderEvent;
        _factory.StripeGateway.WebhookEventsByPayload["replay-newer"] = newerEvent;

        using (var client = _factory.CreateClient())
        {
            var olderTask = client.SendAsync(BuildRequest("replay-older", "t=1,v1=fake"));
            var newerTask = client.SendAsync(BuildRequest("replay-newer", "t=1,v1=fake"));
            await Task.WhenAll(olderTask, newerTask);
        }

        var stateAfterRace = await LoadAsync(companyId);
        Assert.Equal(SubscriptionStatus.PastDue, stateAfterRace.Status);

        // Whichever event physically committed FIRST during the race legitimately got Applied=true at
        // that moment; capture it here rather than assume a fixed winner (see the two tests above for
        // why this is not deterministic under real concurrency) — what the replay below must prove is
        // that redelivering the SAME event id again does not re-evaluate or change that record.
        var appliedBeforeReplay = (await LoadProcessedAsync("evt_replay_older")).Single().Applied;

        // Stripe redelivers the same event id again — the ProcessedStripeEvent unique index makes
        // this a straight idempotency no-op (already recorded, not re-evaluated).
        using (var client = _factory.CreateClient())
        {
            var replay = await client.SendAsync(BuildRequest("replay-older", "t=1,v1=fake"));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        }

        var stateAfterReplay = await LoadAsync(companyId);
        Assert.Equal(SubscriptionStatus.PastDue, stateAfterReplay.Status);
        Assert.Equal(now.AddMonths(3), stateAfterReplay.CurrentPeriodEnd);
        Assert.True(stateAfterReplay.CancelAtPeriodEnd);
        Assert.Equal(stateAfterRace.Version, stateAfterReplay.Version); // no further state change

        var processed = await LoadProcessedAsync("evt_replay_older");
        Assert.Single(processed);
        Assert.Equal(appliedBeforeReplay, processed[0].Applied); // replay did not re-evaluate the record
    }
}
