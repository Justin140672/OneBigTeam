using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.HireCandidate;
using HR.Modules.Recruitment.Features.MoveApplicationForward;
using HR.Modules.Recruitment.Features.MoveApplicationStage;
using HR.Modules.Recruitment.Features.OfferCandidate;
using HR.Modules.Recruitment.Features.RejectCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

// Ticket 6 (P1) optimistic concurrency rollout: Application.Version coverage for the five handlers
// that mutate Application via the non-idempotency-key SaveChangesWithConcurrencyAsync branch. Two
// DbContext instances over the same EF InMemory database mirror UpdateCandidateConcurrencyHandlerTests
// - context B (the "winner") saves first, bumping the store's Version; the handler under test then
// saves against context A with the stale Version it read before the winner committed, which
// SaveChangesWithConcurrencyAsync must translate into a clean Error.Concurrency ("concurrency")
// failure rather than a raw DbUpdateConcurrencyException, with NO partial state committed.
public class ApplicationConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

    private static DbContextOptions<RecruitmentDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(dbName).Options;

    private sealed record Seed(string DbName, Guid CompanyId, Guid VacancyId, Guid CandidateId, Guid ApplicationId, RecruitmentStageTestData.SeededStages Stages);

    private static async Task<Seed> SeedAsync(Func<RecruitmentStageTestData.SeededStages, RecruitmentStage> currentStage)
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        await using var seed = new RecruitmentDbContext(Options(dbName));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(seed, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", "+44 7700 900001", null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, currentStage(stages).Id, null, Now);
        seed.Vacancies.Add(vacancy);
        seed.Candidates.Add(candidate);
        seed.Applications.Add(application);
        await seed.SaveChangesAsync();
        return new Seed(dbName, companyId, vacancy.Id, candidate.Id, application.Id, stages);
    }

    // ---- MoveApplicationStageHandler --------------------------------------------------------

    private static MoveApplicationStageHandler MoveStageHandler(RecruitmentDbContext db, FakeAuditPublisher? audit = null, FakeIntegrationEventPublisher? events = null) =>
        new(db, new FakeClock(FixedUtcNow),
            new RecruitmentStageChangeRecorder(db, events ?? new FakeIntegrationEventPublisher(), audit ?? new FakeAuditPublisher()));

    [Fact]
    public async Task MoveApplicationStage_Stale_Version_Returns_Concurrency_Failure_And_Commits_Only_The_Winner()
    {
        var seed = await SeedAsync(s => s.ApplicationReceived);

        await using var ctxA = new RecruitmentDbContext(Options(seed.DbName));
        await ctxA.Applications.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(seed.DbName)))
        {
            var winner = await MoveStageHandler(ctxB).HandleAsync(
                new MoveApplicationStageRequest
                {
                    CompanyId = seed.CompanyId,
                    VacancyId = seed.VacancyId,
                    ApplicationId = seed.ApplicationId,
                    NewStageId = seed.Stages.CvReview.Id,
                },
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var events = new FakeIntegrationEventPublisher();
        var audit = new FakeAuditPublisher();
        var loser = await MoveStageHandler(ctxA, audit, events).HandleAsync(
            new MoveApplicationStageRequest
            {
                CompanyId = seed.CompanyId,
                VacancyId = seed.VacancyId,
                ApplicationId = seed.ApplicationId,
                NewStageId = seed.Stages.Interview.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(loser.IsFailure);
        Assert.Equal("concurrency", loser.Error.Code);
        Assert.Empty(events.PublishedEvents);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(seed.DbName));
        var saved = await verify.Applications.SingleAsync();
        Assert.Equal(seed.Stages.CvReview.Id, saved.CurrentStageId);
        Assert.Equal(2, saved.Version);
    }

    // ---- HireCandidateHandler ----------------------------------------------------------------

    private static HireCandidateHandler HireHandler(
        RecruitmentDbContext db,
        FakeEmployeeProvisioningService? provisioning = null,
        FakePositionProfileReader? reader = null,
        FakeAuditPublisher? audit = null,
        FakeIntegrationEventPublisher? events = null) =>
        new(
            db,
            provisioning ?? new FakeEmployeeProvisioningService(),
            reader ?? new FakePositionProfileReader(),
            new FakeClock(FixedUtcNow),
            events ?? new FakeIntegrationEventPublisher(),
            audit ?? new FakeAuditPublisher(),
            new RecruitmentStageChangeRecorder(db, events ?? new FakeIntegrationEventPublisher(), audit ?? new FakeAuditPublisher()));

    private static FakePositionProfileReader ResolvableReader(Guid positionProfileId, out Guid departmentId, out Guid locationId)
    {
        departmentId = Guid.NewGuid();
        locationId = Guid.NewGuid();
        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Senior Software Engineer", departmentId, null, true, locationId, "London"),
        };
        return new FakePositionProfileReader(summaries: summaries);
    }

    private static HireCandidateRequest HireRequest(Guid companyId, Guid vacancyId, Guid applicationId) => new()
    {
        CompanyId = companyId,
        VacancyId = vacancyId,
        ApplicationId = applicationId,
        StartDate = new DateOnly(2026, 10, 1),
        DateOfBirth = new DateOnly(1995, 3, 20),
        Nationality = "British",
        Gender = "Female",
    };

    [Fact]
    public async Task HireCandidate_Stale_Version_Returns_Concurrency_Failure_And_Application_Candidate_Remain_Consistent()
    {
        var seed = await SeedAsync(s => s.Offer);

        await using var ctxA = new RecruitmentDbContext(Options(seed.DbName));
        var vacancy = await ctxA.Vacancies.SingleAsync();
        var reader = ResolvableReader(vacancy.PositionProfileId, out _, out _);
        await ctxA.Applications.SingleAsync();
        await ctxA.Candidates.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(seed.DbName)))
        {
            // A concurrent RejectCandidate wins the race first, taking the application to Rejected.
            var rejectWinner = await new RejectCandidateHandler(ctxB, new FakeClock(FixedUtcNow), new RecruitmentStageChangeRecorder(ctxB, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()))
                .HandleAsync(
                    new RejectCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, ApplicationId = seed.ApplicationId },
                    Guid.NewGuid(), CancellationToken.None);
            Assert.True(rejectWinner.IsSuccess);
        }

        var provisioning = new FakeEmployeeProvisioningService();
        var hireLoser = await HireHandler(ctxA, provisioning, reader).HandleAsync(
            HireRequest(seed.CompanyId, seed.VacancyId, seed.ApplicationId), Guid.NewGuid(), CancellationToken.None);

        Assert.True(hireLoser.IsFailure);
        Assert.Equal("concurrency", hireLoser.Error.Code);

        // The Application's own save was rejected wholesale by the concurrency check — it must still
        // reflect the transition that actually committed (Rejected).
        //
        // NOTE: unlike a real relational provider, EF Core's InMemory provider does not apply
        // SaveChangesAsync's multiple entity updates as a single atomic transaction — a
        // DbUpdateConcurrencyException on one tracked entity (Application) does not guarantee an
        // already-applied change to a different tracked entity (Candidate, mutated earlier in the
        // same handler call via candidate.LinkToEmployee) is rolled back too. Against real Postgres
        // (see the integration test covering this exact Hire-vs-Reject race with independent
        // connections) SaveChanges is one transaction, so a concurrency failure on Application
        // guarantees Candidate is untouched as well. Only the Application-level guarantee — the one
        // this handler explicitly enforces — is asserted at the unit level here.
        await using var verify = new RecruitmentDbContext(Options(seed.DbName));
        var savedApplication = await verify.Applications.SingleAsync();
        Assert.Equal(seed.Stages.Rejected.Id, savedApplication.CurrentStageId);
    }

    [Fact]
    public async Task HireCandidate_Stale_Version_Against_Concurrent_Hire_Returns_Concurrency_Failure_And_Commits_Only_The_Winner()
    {
        var seed = await SeedAsync(s => s.Offer);

        await using var ctxA = new RecruitmentDbContext(Options(seed.DbName));
        var vacancyA = await ctxA.Vacancies.SingleAsync();
        var readerA = ResolvableReader(vacancyA.PositionProfileId, out _, out _);
        await ctxA.Applications.SingleAsync();
        // Pre-track the Candidate too (identity-map trick, mirroring the Application above): without
        // this, HireHandler's own Candidate query on ctxA would hit the InMemory store fresh and pick
        // up the winner's already-committed EmployeeId link, turning this into a "conflict" (candidate
        // already linked) rather than the "concurrency" case this test targets.
        await ctxA.Candidates.SingleAsync();

        Guid winningEmployeeId;
        await using (var ctxB = new RecruitmentDbContext(Options(seed.DbName)))
        {
            var vacancyB = await ctxB.Vacancies.SingleAsync();
            var readerB = ResolvableReader(vacancyB.PositionProfileId, out _, out _);
            var provisioningB = new FakeEmployeeProvisioningService();
            var winner = await HireHandler(ctxB, provisioningB, readerB).HandleAsync(
                HireRequest(seed.CompanyId, seed.VacancyId, seed.ApplicationId), Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
            winningEmployeeId = winner.Value!.EmployeeId;
        }

        var provisioningA = new FakeEmployeeProvisioningService();
        var loser = await HireHandler(ctxA, provisioningA, readerA).HandleAsync(
            HireRequest(seed.CompanyId, seed.VacancyId, seed.ApplicationId), Guid.NewGuid(), CancellationToken.None);

        Assert.True(loser.IsFailure);
        Assert.Equal("concurrency", loser.Error.Code);

        // See the NOTE in HireCandidate_Stale_Version_Returns_Concurrency_Failure_And_Application_Candidate_Remain_Consistent
        // above re: EF InMemory not applying SaveChanges atomically across entities — only the
        // Application-level guarantee is asserted at the unit level; the full cross-entity guarantee
        // is covered by the Postgres integration test.
        await using var verify = new RecruitmentDbContext(Options(seed.DbName));
        var savedApplication = await verify.Applications.SingleAsync();
        Assert.Equal(seed.Stages.Hired.Id, savedApplication.CurrentStageId);
    }

    // ---- RejectCandidateHandler ---------------------------------------------------------------

    [Fact]
    public async Task RejectCandidate_Stale_Version_Returns_Concurrency_Failure_And_Writes_Nothing()
    {
        var seed = await SeedAsync(s => s.ApplicationReceived);

        await using var ctxA = new RecruitmentDbContext(Options(seed.DbName));
        await ctxA.Applications.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(seed.DbName)))
        {
            var winner = await new RejectCandidateHandler(ctxB, new FakeClock(FixedUtcNow), new RecruitmentStageChangeRecorder(ctxB, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()))
                .HandleAsync(
                    new RejectCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, ApplicationId = seed.ApplicationId, RejectionReason = "Winner" },
                    Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var loser = await new RejectCandidateHandler(ctxA, new FakeClock(FixedUtcNow), new RecruitmentStageChangeRecorder(ctxA, new FakeIntegrationEventPublisher(), new FakeAuditPublisher()))
            .HandleAsync(
                new RejectCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, ApplicationId = seed.ApplicationId, RejectionReason = "Loser" },
                Guid.NewGuid(), CancellationToken.None);

        Assert.True(loser.IsFailure);
        Assert.Equal("concurrency", loser.Error.Code);

        await using var verify = new RecruitmentDbContext(Options(seed.DbName));
        var saved = await verify.Applications.SingleAsync();
        Assert.Equal("Winner", saved.RejectionReason);
        Assert.Equal(2, saved.Version);
    }

    // ---- OfferCandidateHandler ------------------------------------------------------------------

    private static OfferCandidateHandler OfferHandler(RecruitmentDbContext db, FakeAuditPublisher? audit = null) =>
        new(
            db,
            new FakeClock(FixedUtcNow),
            new FakePositionProfileReader(),
            new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), audit ?? new FakeAuditPublisher()),
            new FakeCompanyRecruitmentSettingsReader(),
            audit ?? new FakeAuditPublisher());

    [Fact]
    public async Task OfferCandidate_Stale_Version_Returns_Concurrency_Failure_And_Writes_Nothing()
    {
        var seed = await SeedAsync(s => s.Interview);

        await using var ctxA = new RecruitmentDbContext(Options(seed.DbName));
        await ctxA.Applications.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(seed.DbName)))
        {
            var winner = await OfferHandler(ctxB).HandleAsync(
                new OfferCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, ApplicationId = seed.ApplicationId },
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var loser = await OfferHandler(ctxA, audit).HandleAsync(
            new OfferCandidateRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, ApplicationId = seed.ApplicationId },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(loser.IsFailure);
        Assert.Equal("concurrency", loser.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(seed.DbName));
        var saved = await verify.Applications.SingleAsync();
        Assert.Equal(seed.Stages.Offer.Id, saved.CurrentStageId);
        Assert.Equal(2, saved.Version);
    }

    // ---- MoveApplicationForwardHandler ------------------------------------------------------------

    private static MoveApplicationForwardHandler ForwardHandler(RecruitmentDbContext db, FakeAuditPublisher? audit = null) =>
        new(
            db,
            new FakeClock(FixedUtcNow),
            audit ?? new FakeAuditPublisher(),
            new RecruitmentStageChangeRecorder(db, new FakeIntegrationEventPublisher(), audit ?? new FakeAuditPublisher()));

    [Fact]
    public async Task MoveApplicationForward_Stale_Version_Returns_Concurrency_Failure_And_Writes_Nothing()
    {
        var seed = await SeedAsync(s => s.ApplicationReceived);

        await using var ctxA = new RecruitmentDbContext(Options(seed.DbName));
        await ctxA.Applications.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(seed.DbName)))
        {
            var winner = await ForwardHandler(ctxB).HandleAsync(
                new MoveApplicationForwardRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, ApplicationId = seed.ApplicationId },
                Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var loser = await ForwardHandler(ctxA, audit).HandleAsync(
            new MoveApplicationForwardRequest { CompanyId = seed.CompanyId, VacancyId = seed.VacancyId, ApplicationId = seed.ApplicationId, CvReviewNotes = "Loser notes" },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(loser.IsFailure);
        Assert.Equal("concurrency", loser.Error.Code);
        Assert.Empty(audit.Published);

        await using var verify = new RecruitmentDbContext(Options(seed.DbName));
        var saved = await verify.Applications.SingleAsync();
        Assert.Equal(seed.Stages.CvReview.Id, saved.CurrentStageId);
        Assert.Null(saved.CvReviewNotes);
        Assert.Equal(2, saved.Version);
    }
}
