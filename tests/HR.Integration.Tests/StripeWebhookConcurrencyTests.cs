using System.Net;
using System.Net.Http.Headers;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Companies.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

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
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);

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
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);

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
        Assert.Equal(SubscriptionStatus.Canceled, persisted.Status);
        Assert.True(persisted.CancelAtPeriodEnd);

        var processed = await LoadProcessedAsync("evt_race_update", "evt_race_delete");
        Assert.Equal(2, processed.Count);
    }

    [Fact]
    public async Task Concurrent_delivery_of_the_same_event_id_applies_exactly_once()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_race_3", "sub_race_3");
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);

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
        Assert.Equal(3, persisted.Version);

        var processed = await LoadProcessedAsync("evt_race_dup");
        Assert.Single(processed);
        Assert.True(processed[0].Applied);
    }

    [Fact]
    public async Task Concurrent_equal_timestamp_events_converge_on_the_deterministic_tiebreak_winner()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_race_4", "sub_race_4");
        var now = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);
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

        // Ticket 9 (P2): the two concurrent events share the same creation timestamp, so whichever
        // one commits SECOND now hits the ambiguous-tie reconciliation path (see
        // CustomerSubscription.IsAmbiguousWithLastApplied) rather than an arbitrary event-id
        // tie-break — it reconciles against this configured live Stripe snapshot instead of either
        // payload's own fields.
        var authoritativePeriodEnd = now.AddMonths(6);
        _factory.StripeGateway.SubscriptionSnapshotsById["sub_race_4"] = new StripeSubscriptionSnapshot(
            "sub_race_4", "cus_race_4", "past_due", authoritativePeriodEnd, CancelAtPeriodEnd: true, PriceId: null);

        using var client = _factory.CreateClient();
        var lowerTask = client.SendAsync(BuildRequest("lower-payload", "t=1,v1=fake"));
        var higherTask = client.SendAsync(BuildRequest("higher-payload", "t=1,v1=fake"));
        var responses = await Task.WhenAll(lowerTask, higherTask);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var persisted = await LoadAsync(companyId);
        Assert.Equal(SubscriptionStatus.PastDue, persisted.Status);
        Assert.Equal(authoritativePeriodEnd, persisted.CurrentPeriodEnd);
        Assert.True(persisted.CancelAtPeriodEnd);

        var processed = await LoadProcessedAsync("evt_aaa_tie", "evt_zzz_tie");
        Assert.Equal(2, processed.Count);
    }

    [Fact]
    public async Task Replaying_the_losing_event_after_a_concurrency_conflict_stays_a_noop()
    {
        var companyId = await SeedCompanyWithSubscriptionAsync("cus_race_5", "sub_race_5");
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

        var appliedBeforeReplay = (await LoadProcessedAsync("evt_replay_older")).Single().Applied;

        using (var client = _factory.CreateClient())
        {
            var replay = await client.SendAsync(BuildRequest("replay-older", "t=1,v1=fake"));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        }

        var stateAfterReplay = await LoadAsync(companyId);
        Assert.Equal(SubscriptionStatus.PastDue, stateAfterReplay.Status);
        Assert.Equal(now.AddMonths(3), stateAfterReplay.CurrentPeriodEnd);
        Assert.True(stateAfterReplay.CancelAtPeriodEnd);
        Assert.Equal(stateAfterRace.Version, stateAfterReplay.Version);

        var processed = await LoadProcessedAsync("evt_replay_older");
        Assert.Single(processed);
        Assert.Equal(appliedBeforeReplay, processed[0].Applied);
    }
}
