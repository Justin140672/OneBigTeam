using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Persistence;
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
/// Ticket 16 (P2): every scenario below now runs behind <see cref="ApplicationSaveBarrier"/>, a
/// deterministic "both racers hold the same stale version before either saves" synchronisation
/// primitive (see its own doc comment for how/why), instead of a bare, unsynchronised
/// <c>Task.WhenAll</c>. Previously a Hire-vs-Reject and an Offer-vs-generic-Move racing variant were
/// attempted and DROPPED here as flaky: with genuinely concurrent, unsynchronised requests, whichever
/// side loses the race could legitimately hit a stage-eligibility business-validation failure (not a
/// version conflict) if its read happened to occur strictly after the winner's write had already
/// committed, rather than racing the SAME stale version. The barrier eliminates that ambiguity by
/// holding both requests at their pre-save point until both have loaded the identical stale version,
/// so Postgres's own optimistic-concurrency check is what decides the winner — never test timing. Both
/// scenarios are therefore included below, fully deterministic.
///
/// Full deterministic coverage for every handler (Hire/Offer/Reject/both Move endpoints) — using
/// forced version staleness rather than a live race — is also in
/// HR.Modules.Recruitment.Tests/ApplicationConcurrencyHandlerTests.cs; this file additionally proves
/// the live-Postgres race + idempotency-record + key-reuse-retry + Employee-non-duplication behaviour
/// that only a real overlapping write can exercise.
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

        HttpResponseMessage responseA, responseB;
        using (ApplicationSaveBarrier.Arm(seeded.ApplicationId))
        {
            var taskA = client.SendAsync(requestA);
            var taskB = client.SendAsync(requestB);

            var responses = await Task.WhenAll(taskA, taskB);
            responseA = responses[0];
            responseB = responses[1];
        }

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

    [Fact]
    public async Task Hire_Vs_Reject_Racing_On_Same_Application_Exactly_One_Wins_With_Consistent_State()
    {
        var companyId = Guid.NewGuid();
        var client = await ClientAs(companyId);
        var now = DateTimeOffset.UtcNow;

        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(
            _factory, companyId, now, positionProfileId: referenceData.PositionProfileId);

        var keyHire = $"idem-hire-{Guid.NewGuid():N}";
        var keyReject = $"idem-reject-{Guid.NewGuid():N}";

        object HireBody() => new
        {
            companyId,
            vacancyId = seeded.VacancyId,
            applicationId = seeded.ApplicationId,
            startDate = new DateOnly(2026, 10, 1).ToString("yyyy-MM-dd"),
            dateOfBirth = new DateOnly(1990, 1, 1).ToString("yyyy-MM-dd"),
            nationality = "British",
            gender = "Prefer not to say",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId = referenceData.EmploymentTypeId,
        };

        var hireRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/hire",
            HireBody(), keyHire);
        var rejectRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/reject",
            new { companyId, rejectionReason = "Role filled by another candidate" }, keyReject);

        HttpResponseMessage hireResponse, rejectResponse;
        using (ApplicationSaveBarrier.Arm(seeded.ApplicationId))
        {
            var hireTask = client.SendAsync(hireRequest);
            var rejectTask = client.SendAsync(rejectRequest);

            var responses = await Task.WhenAll(hireTask, rejectTask);
            hireResponse = responses[0];
            rejectResponse = responses[1];
        }

        Assert.True(
            hireResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected hire status: {hireResponse.StatusCode}");
        Assert.True(
            rejectResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected reject status: {rejectResponse.StatusCode}");

        var succeeded = new[] { hireResponse, rejectResponse }.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicted = new[] { hireResponse, rejectResponse }.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        // The barrier guarantees a real version conflict (never a validation-shaped rejection): the
        // loser must be a clean 409 "concurrency", not e.g. a 400 for hitting the terminal-stage guard.
        var hireWon = hireResponse.StatusCode == HttpStatusCode.OK;
        var loserResponse = hireWon ? rejectResponse : hireResponse;
        Assert.Equal(HttpStatusCode.Conflict, loserResponse.StatusCode);
        var loserProblem = await loserResponse.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", loserProblem!.Code);

        using var recruitmentScope = _factory.Services.CreateScope();
        var recruitmentDb = recruitmentScope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var savedApplication = await recruitmentDb.Applications.AsNoTracking().SingleAsync(a => a.Id == seeded.ApplicationId);
        var savedCandidate = await recruitmentDb.Candidates.AsNoTracking().SingleAsync(c => c.Id == seeded.CandidateId);
        var hiredStageId = await recruitmentDb.RecruitmentStages.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.Name == "Hired").Select(s => s.Id).SingleAsync();
        var rejectedStageId = await recruitmentDb.RecruitmentStages.AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.Name == "Rejected").Select(s => s.Id).SingleAsync();

        if (hireWon)
        {
            Assert.Equal(hiredStageId, savedApplication.CurrentStageId);
            Assert.NotNull(savedCandidate.EmployeeId);

            var hirePayload = await hireResponse.Content.ReadFromJsonAsync<HirePayload>();
            Assert.Equal(savedCandidate.EmployeeId, hirePayload!.EmployeeId);
        }
        else
        {
            Assert.Equal(rejectedStageId, savedApplication.CurrentStageId);
            Assert.Null(savedCandidate.EmployeeId);
        }

        var historyCount = await recruitmentDb.ApplicationStageHistoryEntries.CountAsync(h => h.ApplicationId == seeded.ApplicationId);
        Assert.Equal(1, historyCount);

        var loserKey = hireWon ? keyReject : keyHire;
        Assert.False(await recruitmentDb.IdempotencyRecords.AnyAsync(r => r.Key == loserKey));
    }

    [Fact]
    public async Task Offer_Vs_Generic_Move_Racing_On_Same_Application_Exactly_One_Wins_With_Consistent_State()
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

        var keyOffer = $"idem-offer-{Guid.NewGuid():N}";
        var keyMove = $"idem-move-{Guid.NewGuid():N}";

        // Both are independently valid from the seeded starting stage ("CV Review", non-terminal):
        // OfferCandidateHandler resolves its own target stage (the company's Purpose == Offer stage)
        // internally, and MoveApplicationStage targets "Interview" — a distinct, active, non-terminal
        // stage — so neither request depends on the other's outcome to be individually legal.
        var offerRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/offer",
            new { companyId, vacancyId = seeded.VacancyId, applicationId = seeded.ApplicationId }, keyOffer);
        var moveRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/move-stage",
            new { companyId, newStageId = interviewStageId }, keyMove);

        HttpResponseMessage offerResponse, moveResponse;
        using (ApplicationSaveBarrier.Arm(seeded.ApplicationId))
        {
            var offerTask = client.SendAsync(offerRequest);
            var moveTask = client.SendAsync(moveRequest);

            var responses = await Task.WhenAll(offerTask, moveTask);
            offerResponse = responses[0];
            moveResponse = responses[1];
        }

        Assert.True(
            offerResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected offer status: {offerResponse.StatusCode}");
        Assert.True(
            moveResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected move status: {moveResponse.StatusCode}");

        var succeeded = new[] { offerResponse, moveResponse }.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicted = new[] { offerResponse, moveResponse }.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        var offerWon = offerResponse.StatusCode == HttpStatusCode.OK;
        var loserResponse = offerWon ? moveResponse : offerResponse;
        var loserProblem = await loserResponse.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", loserProblem!.Code);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var savedApplication = await verifyDb.Applications.AsNoTracking().SingleAsync(a => a.Id == seeded.ApplicationId);
        Assert.Equal(offerWon ? offerStageId : interviewStageId, savedApplication.CurrentStageId);

        var historyCount = await verifyDb.ApplicationStageHistoryEntries.CountAsync(h => h.ApplicationId == seeded.ApplicationId);
        Assert.Equal(1, historyCount);

        var loserKey = offerWon ? keyMove : keyOffer;
        Assert.False(await verifyDb.IdempotencyRecords.AnyAsync(r => r.Key == loserKey));
    }

    /// <summary>
    /// Proves the documented "same key safely reusable after a concurrency conflict" policy (see
    /// DbContextIdempotencyExtensions.SaveIdempotentWithConcurrencyAsync's doc comment): a
    /// ConcurrencyConflict commits nothing, including no idempotency record, so a caller may reload and
    /// retry with the SAME Idempotency-Key rather than having to mint a new one.
    /// </summary>
    [Fact]
    public async Task Losing_Key_Can_Be_Safely_Reused_For_A_Corrected_Retry_After_Reload()
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

        var keyA = $"idem-reuse-a-{Guid.NewGuid():N}";
        var keyB = $"idem-reuse-b-{Guid.NewGuid():N}";

        var requestA = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/move-stage",
            new { companyId, newStageId = interviewStageId }, keyA);
        var requestB = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/move-stage",
            new { companyId, newStageId = offerStageId }, keyB);

        HttpResponseMessage responseA, responseB;
        using (ApplicationSaveBarrier.Arm(seeded.ApplicationId))
        {
            var taskA = client.SendAsync(requestA);
            var taskB = client.SendAsync(requestB);
            var responses = await Task.WhenAll(taskA, taskB);
            responseA = responses[0];
            responseB = responses[1];
        }

        var aWon = responseA.StatusCode == HttpStatusCode.OK;
        var loserKey = aWon ? keyB : keyA;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            Assert.False(await db.IdempotencyRecords.AnyAsync(r => r.Key == loserKey));
        }

        // "Reload" - re-read current state - then build a corrected retry that is valid against it:
        // the application is now on whichever stage won, so move it on to "Hired"'s sibling non-terminal
        // stage ("Interview" if Offer won, "Offer" if Interview won) using the SAME (losing) key.
        Guid retryTargetStageId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var current = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == seeded.ApplicationId);
            retryTargetStageId = current.CurrentStageId == interviewStageId ? offerStageId : interviewStageId;
        }

        var retryRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/move-stage",
            new { companyId, newStageId = retryTargetStageId }, loserKey);

        var retryResponse = await client.SendAsync(retryRequest);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var recordCount = await db.IdempotencyRecords.CountAsync(r => r.Key == loserKey);
            Assert.Equal(1, recordCount);

            var finalApplication = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == seeded.ApplicationId);
            Assert.Equal(retryTargetStageId, finalApplication.CurrentStageId);
        }
    }

    /// <summary>
    /// Runs the Hire-vs-Reject race from <see cref="Hire_Vs_Reject_Racing_On_Same_Application_Exactly_One_Wins_With_Consistent_State"/>
    /// but specifically inspects the losing side's outcome and proves the documented, correct
    /// consistency state described by HireCandidateHandler's own NFR-08 remarks:
    ///  - if Hire lost, its pre-provisioned Employee (via the stable SourceReference
    ///    "recruitment:application:{applicationId}") is left orphaned (not linked to any Candidate),
    ///    and a stale Hire retry with the old key hits the handler's own terminal-stage business
    ///    validation (Reject already committed) rather than a concurrency conflict - this is correct,
    ///    expected behaviour, not a bug.
    ///  - either way, exactly ONE Employee row exists for this SourceReference - the pre-provisioned
    ///    employee is never duplicated by a retry.
    /// </summary>
    [Fact]
    public async Task Hire_Retry_After_Losing_To_Reject_Reuses_Same_Employee_And_Hits_Terminal_Stage_Validation()
    {
        var companyId = Guid.NewGuid();
        var client = await ClientAs(companyId);
        var now = DateTimeOffset.UtcNow;

        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var seeded = await RecruitmentTestSeeder.SeedApplicationAsync(
            _factory, companyId, now, positionProfileId: referenceData.PositionProfileId);
        var sourceReference = $"recruitment:application:{seeded.ApplicationId}";

        var keyHire = $"idem-hire-retry-{Guid.NewGuid():N}";
        var keyReject = $"idem-reject-retry-{Guid.NewGuid():N}";

        object HireBody() => new
        {
            companyId,
            vacancyId = seeded.VacancyId,
            applicationId = seeded.ApplicationId,
            startDate = new DateOnly(2026, 10, 1).ToString("yyyy-MM-dd"),
            dateOfBirth = new DateOnly(1990, 1, 1).ToString("yyyy-MM-dd"),
            nationality = "British",
            gender = "Prefer not to say",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId = referenceData.EmploymentTypeId,
        };

        var hireRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/hire",
            HireBody(), keyHire);
        var rejectRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/reject",
            new { companyId, rejectionReason = "Role filled by another candidate" }, keyReject);

        HttpResponseMessage hireResponse, rejectResponse;
        using (ApplicationSaveBarrier.Arm(seeded.ApplicationId))
        {
            var hireTask = client.SendAsync(hireRequest);
            var rejectTask = client.SendAsync(rejectRequest);
            var responses = await Task.WhenAll(hireTask, rejectTask);
            hireResponse = responses[0];
            rejectResponse = responses[1];
        }

        // Regardless of which won, the Employee was already provisioned (the very first thing
        // HireCandidateHandler does, in a separate module/transaction, before either save branch
        // races) - so exactly one Employee row must exist for this SourceReference either way.
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var employeeCount = await employeesDb.Employees.CountAsync(e => e.CompanyId == companyId && e.SourceReference == sourceReference);
            Assert.Equal(1, employeeCount);
        }

        var hireWon = hireResponse.StatusCode == HttpStatusCode.OK;

        if (hireWon)
        {
            // Hire committed outright - nothing further to retry; the Employee is linked as expected.
            using var scope = _factory.Services.CreateScope();
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var savedCandidate = await recruitmentDb.Candidates.AsNoTracking().SingleAsync(c => c.Id == seeded.CandidateId);
            Assert.NotNull(savedCandidate.EmployeeId);
            return;
        }

        // Reject won: the barrier guarantees Reject's UPDATE committed before Hire's own save could
        // possibly succeed, so the Application is now on the terminal "Rejected" stage. A stale Hire
        // retry using the SAME key therefore does not even reach the concurrency-guarded save - it is
        // rejected by HireCandidateHandler's own "Cannot hire an application already on the terminal
        // stage" business validation instead. This is correct, expected behaviour (not a bug): the
        // concurrency guard's job is to catch a stale WRITE, not to re-validate business rules that
        // have since become false.
        var retryHireRequest = BuildIdempotentPostRequest(
            $"/api/companies/{companyId}/vacancies/{seeded.VacancyId}/applications/{seeded.ApplicationId}/hire",
            HireBody(), keyHire);
        var retryHireResponse = await client.SendAsync(retryHireRequest);

        Assert.Equal(HttpStatusCode.BadRequest, retryHireResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var savedCandidate = await recruitmentDb.Candidates.AsNoTracking().SingleAsync(c => c.Id == seeded.CandidateId);
            // Never linked - the orphaned pre-provisioned Employee stays orphaned; this is the
            // documented reconciliation state, not something this test invents.
            Assert.Null(savedCandidate.EmployeeId);
        }

        using (var employeesScope = _factory.Services.CreateScope())
        {
            var employeesDb = employeesScope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var employeeCount = await employeesDb.Employees.CountAsync(e => e.CompanyId == companyId && e.SourceReference == sourceReference);
            Assert.Equal(1, employeeCount);
        }
    }

    private sealed record HirePayload(Guid Id, Guid EmployeeId);
    private sealed record ErrorPayload(string? Error, string? Code);
}
