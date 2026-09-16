using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 14 (P2): PostgreSQL, real-connection coverage proving that supplying an "Idempotency-Key"
/// header on a Recruitment application-transition request does NOT bypass optimistic-concurrency
/// protection or its controlled-conflict translation. Before this fix, the idempotent-keyed branch of
/// HireCandidate/OfferCandidate/RejectCandidate/MoveApplicationStage/MoveApplicationForward called
/// plain SaveIdempotentAsync, which neither pinned/advanced Application.Version nor translated a
/// stale-save DbUpdateConcurrencyException — so two idempotent-keyed writes racing on the same
/// Application never actually conflicted (silently applying both / losing an update), and any write that
/// DID race a genuinely concurrent non-idempotent saver surfaced as an unhandled 500 instead of the
/// same clean 409 ("concurrency") every non-idempotent caller already gets.
///
/// Mirrors ApplicationConcurrencyEndpointTests's Task.WhenAll-driven real-overlapping-write pattern,
/// with an "Idempotency-Key" header added to every request (see CreateEmployeeEndpointTests's
/// BuildIdempotentPostRequest for the header-attaching convention used elsewhere in this project) and
/// an additional assertion that a lost race never leaves behind an idempotency record under the
/// loser's key — a real conflict must be freely retryable with the same key once the caller reloads.
/// </summary>
[Collection("Integration")]
public class IdempotentApplicationTransitionConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc0f00c6-0000-0000-0000-000000000002");

    public IdempotentApplicationTransitionConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, RecruiterUser, companyId);
        return client;
    }

    private static HttpRequestMessage BuildIdempotentPostRequest(string url, object body, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    // NOTE: a Hire-vs-Reject and an Offer-vs-generic-Move racing variant were both attempted here but
    // dropped — with genuinely concurrent (Task.WhenAll, no forced ordering) requests, whichever side
    // loses the race can legitimately hit a stage-eligibility business-validation failure (not a
    // version conflict) if its read happens to occur strictly after the winner's write already
    // committed, rather than racing the SAME stale version — an inherent property of true concurrency
    // without synchronisation, not a defect in this fix. Two_Idempotent_Generic_Moves_Racing below
    // (same target-stage-agnostic transition on both sides) does not have this precondition-ordering
    // sensitivity and reliably exercises the same ConcurrencyConflict path against real PostgreSQL.
    // Full deterministic coverage for every handler (Hire/Offer/Reject/both Move endpoints) — using
    // forced version staleness rather than a live race — is in
    // HR.Modules.Recruitment.Tests/ApplicationConcurrencyHandlerTests.cs.
    [Fact]
    public async Task Two_Idempotent_Generic_Moves_Racing_On_Same_Application_Exactly_One_Wins_Not_500()
    {
        var companyId = Guid.NewGuid();
        var client = await ClientAs(companyId);
        var now = DateTimeOffset.UtcNow;

        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(_factory, companyId, now);

        Guid interviewStageId, offerStageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            interviewStageId = await db.RecruitmentStages.AsNoTracking()
                .Where(s => s.CompanyId == companyId && s.Name == "Interview")
                .Select(s => s.Id)
                .SingleAsync();
            offerStageId = await db.RecruitmentStages.AsNoTracking()
                .Where(s => s.CompanyId == companyId && s.Name == "Offer")
                .Select(s => s.Id)
                .SingleAsync();
        }

        var keyA = $"idem-move-a-{Guid.NewGuid():N}";
        var keyB = $"idem-move-b-{Guid.NewGuid():N}";

        var requestA = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/move-stage",
            new { companyId, newStageId = interviewStageId },
            keyA);

        var requestB = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/move-stage",
            new { companyId, newStageId = offerStageId },
            keyB);

        var taskA = client.SendAsync(requestA);
        var taskB = client.SendAsync(requestB);

        var responses = await Task.WhenAll(taskA, taskB);
        var responseA = responses[0];
        var responseB = responses[1];

        // Never both a clean success (that would mean the loser's stale write silently landed instead
        // of being rejected — an undetected lost update) and never a 500 for either.
        Assert.True(
            responseA.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected status A: {responseA.StatusCode}");
        Assert.True(
            responseB.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected status B: {responseB.StatusCode}");

        var succeeded = new[] { responseA, responseB }.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicted = new[] { responseA, responseB }.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var savedApplication = await verifyDb.Applications.AsNoTracking().SingleAsync(a => a.Id == seeded.ApplicationId);

        var aWon = responseA.StatusCode == HttpStatusCode.OK;
        Assert.Equal(aWon ? interviewStageId : offerStageId, savedApplication.CurrentStageId);

        var historyCount = await verifyDb.ApplicationStageHistoryEntries.CountAsync(h => h.ApplicationId == seeded.ApplicationId);
        Assert.Equal(1, historyCount);

        var loserKey = aWon ? keyB : keyA;
        Assert.False(await verifyDb.IdempotencyRecords.AnyAsync(r => r.Key == loserKey));
    }

    private sealed record Payload(Guid Id);
}
