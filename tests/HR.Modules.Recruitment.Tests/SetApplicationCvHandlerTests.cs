using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.SetApplicationCv;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 1: unit coverage (EF InMemory) for SetApplicationCvHandler — attach,
/// replace and remove the CV recorded as submitted with an application. Database-level FK and real
/// Postgres concurrency behaviour is covered separately in ApplicationCvDocumentConstraintTests.
/// </summary>
public class SetApplicationCvHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SeededAt = Now.AddDays(-5);

    private static DbContextOptions<RecruitmentDbContext> Options(string dbName) =>
        new DbContextOptionsBuilder<RecruitmentDbContext>().UseInMemoryDatabase(dbName).Options;

    private sealed record Seed(
        string DbName,
        Guid CompanyId,
        Guid VacancyId,
        Guid CandidateId,
        Guid ApplicationId,
        CandidateDocument Cv1,
        CandidateDocument Cv2);

    private static async Task<Seed> SeedAsync(bool attachCv1 = false)
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        await using var db = new RecruitmentDbContext(Options(dbName));

        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, SeededAt);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), SeededAt);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, SeededAt);
        var cv1 = Cv(companyId, candidate.Id, "cv-v1.pdf", SeededAt);
        var cv2 = Cv(companyId, candidate.Id, "cv-v2.pdf", SeededAt.AddDays(1));
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.CvReview.Id, null, SeededAt);
        if (attachCv1)
            application.AttachCv(cv1, SeededAt);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(cv1, cv2);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        return new Seed(dbName, companyId, vacancy.Id, candidate.Id, application.Id, cv1, cv2);
    }

    private static CandidateDocument Cv(
        Guid companyId, Guid candidateId, string fileName, DateTimeOffset createdAt,
        CandidateDocumentKind kind = CandidateDocumentKind.Cv) =>
        CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidateId, fileName, fileName, 2048, "application/pdf",
            $"{companyId}/{candidateId}/{Guid.NewGuid():N}/{fileName}", Guid.NewGuid(), createdAt, kind);

    private static async Task<CandidateDocument> AddDocumentAsync(string dbName, CandidateDocument document)
    {
        await using var db = new RecruitmentDbContext(Options(dbName));
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    private static SetApplicationCvHandler Handler(RecruitmentDbContext db, FakeAuditPublisher? audit = null) =>
        new(db, new FakeClock(FixedUtcNow), audit ?? new FakeAuditPublisher());

    private static SetApplicationCvRequest Request(Seed seed, Guid? cvDocumentId, int? expectedVersion = 1) => new()
    {
        CompanyId       = seed.CompanyId,
        VacancyId       = seed.VacancyId,
        ApplicationId   = seed.ApplicationId,
        CvDocumentId    = cvDocumentId,
        ExpectedVersion = expectedVersion,
    };

    private static async Task<Application> ReloadAsync(Seed seed)
    {
        await using var verify = new RecruitmentDbContext(Options(seed.DbName));
        return await verify.Applications.AsNoTracking().SingleAsync(a => a.Id == seed.ApplicationId);
    }


    [Fact]
    public async Task HandleAsync_Attaches_Cv_To_Application_Without_A_Reference()
    {
        var seed = await SeedAsync();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db).HandleAsync(Request(seed, seed.Cv1.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.ApplicationId, result.Value!.Id);
        Assert.Equal(seed.VacancyId, result.Value.VacancyId);
        Assert.Equal(seed.CandidateId, result.Value.CandidateId);
        Assert.Equal(seed.Cv1.Id, result.Value.CvDocumentId);
        Assert.Equal(2, result.Value.Version);
        Assert.Equal(Now, result.Value.UpdatedAt);

        var saved = await ReloadAsync(seed);
        Assert.Equal(seed.Cv1.Id, saved.CvDocumentId);
        Assert.Equal(2, saved.Version);
        Assert.Equal(Now, saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Replaces_Existing_Cv_Reference()
    {
        var seed = await SeedAsync(attachCv1: true);
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db).HandleAsync(Request(seed, seed.Cv2.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.Cv2.Id, result.Value!.CvDocumentId);
        Assert.Equal(2, result.Value.Version);

        var saved = await ReloadAsync(seed);
        Assert.Equal(seed.Cv2.Id, saved.CvDocumentId);
    }

    [Fact]
    public async Task HandleAsync_Removes_Cv_Reference_When_CvDocumentId_Is_Null()
    {
        var seed = await SeedAsync(attachCv1: true);
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db).HandleAsync(Request(seed, null), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CvDocumentId);
        Assert.Equal(2, result.Value.Version);

        var saved = await ReloadAsync(seed);
        Assert.Null(saved.CvDocumentId);

        await using var verify = new RecruitmentDbContext(Options(seed.DbName));
        Assert.True(await verify.CandidateDocuments.AnyAsync(d => d.Id == seed.Cv1.Id));
    }

    [Fact]
    public async Task HandleAsync_Successive_Changes_Each_Advance_The_Version()
    {
        var seed = await SeedAsync();

        await using (var db = new RecruitmentDbContext(Options(seed.DbName)))
            Assert.True((await Handler(db).HandleAsync(Request(seed, seed.Cv1.Id, 1), Guid.NewGuid(), CancellationToken.None)).IsSuccess);

        await using (var db = new RecruitmentDbContext(Options(seed.DbName)))
            Assert.True((await Handler(db).HandleAsync(Request(seed, seed.Cv2.Id, 2), Guid.NewGuid(), CancellationToken.None)).IsSuccess);

        await using (var db = new RecruitmentDbContext(Options(seed.DbName)))
        {
            var removed = await Handler(db).HandleAsync(Request(seed, null, 3), Guid.NewGuid(), CancellationToken.None);
            Assert.True(removed.IsSuccess);
            Assert.Equal(4, removed.Value!.Version);
        }

        var saved = await ReloadAsync(seed);
        Assert.Null(saved.CvDocumentId);
        Assert.Equal(4, saved.Version);
    }


    [Fact]
    public async Task HandleAsync_Attach_Publishes_Audit_Event_With_Null_Previous_And_Actor()
    {
        var seed = await SeedAsync();
        var audit = new FakeAuditPublisher();
        var performedBy = Guid.NewGuid();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        await Handler(db, audit).HandleAsync(Request(seed, seed.Cv1.Id), performedBy, CancellationToken.None);

        var evt = Assert.IsType<ApplicationCvReferenceChangedAuditEvent>(Assert.Single(audit.Published));
        Assert.Equal(seed.CompanyId, evt.CompanyId);
        Assert.Equal(seed.ApplicationId, evt.ApplicationId);
        Assert.Equal(seed.VacancyId, evt.VacancyId);
        Assert.Equal(seed.CandidateId, evt.CandidateId);
        Assert.Null(evt.PreviousCvDocumentId);
        Assert.Equal(seed.Cv1.Id, evt.NewCvDocumentId);
        Assert.Equal(performedBy, evt.ChangedByUserId);
        Assert.Equal(Now, evt.OccurredAt);

        IAuditEvent asAudit = evt;
        Assert.Equal("application.cv_reference_changed", asAudit.EventType);
        Assert.Equal("Application", asAudit.EntityType);
        Assert.Equal(seed.ApplicationId, asAudit.EntityId);
        Assert.Equal(performedBy, asAudit.ActorUserId);
        Assert.Equal("Submitted CV recorded for application", asAudit.Summary);
    }

    [Fact]
    public async Task HandleAsync_Replace_Publishes_Audit_Event_With_Previous_And_New_Ids()
    {
        var seed = await SeedAsync(attachCv1: true);
        var audit = new FakeAuditPublisher();
        var performedBy = Guid.NewGuid();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        await Handler(db, audit).HandleAsync(Request(seed, seed.Cv2.Id), performedBy, CancellationToken.None);

        var evt = Assert.IsType<ApplicationCvReferenceChangedAuditEvent>(Assert.Single(audit.Published));
        Assert.Equal(seed.Cv1.Id, evt.PreviousCvDocumentId);
        Assert.Equal(seed.Cv2.Id, evt.NewCvDocumentId);
        Assert.Equal(performedBy, evt.ChangedByUserId);
        Assert.Equal("Submitted CV replaced on application", ((IAuditEvent)evt).Summary);
    }

    [Fact]
    public async Task HandleAsync_Remove_Publishes_Audit_Event_With_Null_New_Id()
    {
        var seed = await SeedAsync(attachCv1: true);
        var audit = new FakeAuditPublisher();
        var performedBy = Guid.NewGuid();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        await Handler(db, audit).HandleAsync(Request(seed, null), performedBy, CancellationToken.None);

        var evt = Assert.IsType<ApplicationCvReferenceChangedAuditEvent>(Assert.Single(audit.Published));
        Assert.Equal(seed.Cv1.Id, evt.PreviousCvDocumentId);
        Assert.Null(evt.NewCvDocumentId);
        Assert.Equal(performedBy, evt.ChangedByUserId);
        Assert.Equal("Submitted CV removed from application", ((IAuditEvent)evt).Summary);
    }


    [Fact]
    public async Task HandleAsync_Attaching_The_Already_Referenced_Cv_Is_A_NoOp()
    {
        var seed = await SeedAsync(attachCv1: true);
        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db, audit).HandleAsync(Request(seed, seed.Cv1.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seed.Cv1.Id, result.Value!.CvDocumentId);
        Assert.Equal(1, result.Value.Version);
        Assert.Equal(SeededAt, result.Value.UpdatedAt);
        Assert.Empty(audit.Published);

        var saved = await ReloadAsync(seed);
        Assert.Equal(1, saved.Version);
        Assert.Equal(SeededAt, saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Removing_When_No_Reference_Is_A_NoOp()
    {
        var seed = await SeedAsync();
        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db, audit).HandleAsync(Request(seed, null), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CvDocumentId);
        Assert.Equal(1, result.Value.Version);
        Assert.Empty(audit.Published);

        var saved = await ReloadAsync(seed);
        Assert.Equal(1, saved.Version);
        Assert.Equal(SeededAt, saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_NoOp_With_Stale_ExpectedVersion_Still_Returns_Concurrency()
    {
        // Stale callers are rejected up front even when the request would change nothing, so the
        // client always learns its view is out of date.
        var seed = await SeedAsync(attachCv1: true);
        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db, audit).HandleAsync(Request(seed, seed.Cv1.Id, expectedVersion: 7), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
    }


    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Application()
    {
        var seed = await SeedAsync();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db).HandleAsync(
            Request(seed, seed.Cv1.Id) with { ApplicationId = Guid.NewGuid() }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Belongs_To_Different_Vacancy()
    {
        var seed = await SeedAsync();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db).HandleAsync(
            Request(seed, seed.Cv1.Id) with { VacancyId = Guid.NewGuid() }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Null((await ReloadAsync(seed)).CvDocumentId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Belongs_To_Different_Company()
    {
        var seed = await SeedAsync();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db).HandleAsync(
            Request(seed, seed.Cv1.Id) with { CompanyId = Guid.NewGuid() }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Null((await ReloadAsync(seed)).CvDocumentId);
    }


    [Fact]
    public async Task HandleAsync_Rejects_Unknown_Document()
    {
        var seed = await SeedAsync();
        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db, audit).HandleAsync(Request(seed, Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        AssertValidationFailure(result, Application.CvDocumentNotFoundMessage);
        Assert.Empty(audit.Published);
        await AssertUnchangedAsync(seed, expectedCvDocumentId: null);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Document_From_Another_Company_As_Not_Found()
    {
        var seed = await SeedAsync();
        var otherCompanyId = Guid.NewGuid();
        var foreignCv = await AddDocumentAsync(seed.DbName, Cv(otherCompanyId, Guid.NewGuid(), "foreign.pdf", SeededAt));
        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db, audit).HandleAsync(Request(seed, foreignCv.Id), Guid.NewGuid(), CancellationToken.None);

        AssertValidationFailure(result, Application.CvDocumentNotFoundMessage);
        Assert.Empty(audit.Published);
        await AssertUnchangedAsync(seed, expectedCvDocumentId: null);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Document_Of_Another_Candidate_In_Same_Company()
    {
        var seed = await SeedAsync();
        var otherCandidatesCv = await AddDocumentAsync(seed.DbName, Cv(seed.CompanyId, Guid.NewGuid(), "someone-else.pdf", SeededAt));
        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db, audit).HandleAsync(Request(seed, otherCandidatesCv.Id), Guid.NewGuid(), CancellationToken.None);

        AssertValidationFailure(result, "The selected CV document belongs to a different candidate.");
        Assert.Empty(audit.Published);
        await AssertUnchangedAsync(seed, expectedCvDocumentId: null);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Non_Cv_Document_Of_Same_Candidate()
    {
        var seed = await SeedAsync();
        var coverLetter = await AddDocumentAsync(
            seed.DbName, Cv(seed.CompanyId, seed.CandidateId, "cover.pdf", SeededAt, CandidateDocumentKind.Other));
        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db, audit).HandleAsync(Request(seed, coverLetter.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("not a CV", result.Error.Message);
        Assert.Empty(audit.Published);
        await AssertUnchangedAsync(seed, expectedCvDocumentId: null);
    }

    [Fact]
    public async Task HandleAsync_Invalid_Replacement_Leaves_Existing_Reference_Intact()
    {
        var seed = await SeedAsync(attachCv1: true);
        var otherCandidatesCv = await AddDocumentAsync(seed.DbName, Cv(seed.CompanyId, Guid.NewGuid(), "someone-else.pdf", SeededAt));
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db).HandleAsync(Request(seed, otherCandidatesCv.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        await AssertUnchangedAsync(seed, expectedCvDocumentId: seed.Cv1.Id);
    }


    [Fact]
    public async Task HandleAsync_Returns_Concurrency_When_ExpectedVersion_Is_Stale()
    {
        var seed = await SeedAsync();
        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db, audit).HandleAsync(Request(seed, seed.Cv1.Id, expectedVersion: 0), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
        await AssertUnchangedAsync(seed, expectedCvDocumentId: null);
    }

    [Fact]
    public async Task HandleAsync_Returns_Concurrency_When_ExpectedVersion_Is_Ahead_Of_Current()
    {
        var seed = await SeedAsync();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db).HandleAsync(Request(seed, seed.Cv1.Id, expectedVersion: 2), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Without_ExpectedVersion_Is_Rejected_As_Concurrency_And_Saves_Nothing()
    {
        var seed = await SeedAsync();
        var audit = new FakeAuditPublisher();
        await using var db = new RecruitmentDbContext(Options(seed.DbName));

        var result = await Handler(db, audit).HandleAsync(Request(seed, seed.Cv1.Id, expectedVersion: null), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.Published);
        await AssertUnchangedAsync(seed, expectedCvDocumentId: null);
    }

    [Fact]
    public async Task HandleAsync_Losing_A_Concurrent_Save_Returns_Concurrency_And_Commits_Only_The_Winner()
    {
        var seed = await SeedAsync();

        await using var ctxA = new RecruitmentDbContext(Options(seed.DbName));
        await ctxA.Applications.SingleAsync();

        await using (var ctxB = new RecruitmentDbContext(Options(seed.DbName)))
        {
            var winner = await Handler(ctxB).HandleAsync(Request(seed, seed.Cv1.Id), Guid.NewGuid(), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditPublisher();
        var loser = await Handler(ctxA, audit).HandleAsync(Request(seed, seed.Cv2.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.True(loser.IsFailure);
        Assert.Equal("concurrency", loser.Error.Code);
        Assert.Empty(audit.Published);

        var saved = await ReloadAsync(seed);
        Assert.Equal(seed.Cv1.Id, saved.CvDocumentId);
        Assert.Equal(2, saved.Version);
    }


    private static void AssertValidationFailure(Result<SetApplicationCvResponse> result, string expectedMessage)
    {
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(expectedMessage, result.Error.Message);
    }

    private static async Task AssertUnchangedAsync(Seed seed, Guid? expectedCvDocumentId)
    {
        var saved = await ReloadAsync(seed);
        Assert.Equal(expectedCvDocumentId, saved.CvDocumentId);
        Assert.Equal(1, saved.Version);
        Assert.Equal(SeededAt, saved.UpdatedAt);
    }
}
