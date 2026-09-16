using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.PurgeEligibleCandidates;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class PurgeEligibleCandidatesHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static Candidate CreateCandidateUpdatedAt(Guid companyId, DateTimeOffset updatedAt)
    {
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, updatedAt);
        // UpdatedAt on Candidate is set by Create(now) — CreateCandidate's "now" arg becomes both
        // CreatedAt and UpdatedAt, which is sufficient for eligibility calculations here.
        return candidate;
    }

    [Fact]
    public async Task HandleAsync_Purges_Candidate_Past_Retention_Window_With_No_Open_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var purgedBy = Guid.NewGuid();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            purgedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PurgedCount);

        var saved = await db.Candidates.SingleAsync();
        Assert.NotNull(saved.PurgedAt);
        Assert.Equal(purgedBy, saved.PurgedByUserId);
        Assert.Equal("[purged]", saved.FirstName);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<CandidatesPurgedAuditEvent>(published);
        Assert.Equal(new[] { candidate.Id }, auditEvent.PurgedCandidateIds);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Purge_Candidate_Within_Retention_Window()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recent = Now.AddDays(-10);
        var candidate = CreateCandidateUpdatedAt(companyId, recent);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.PurgedCount);

        var saved = await db.Candidates.SingleAsync();
        Assert.Null(saved.PurgedAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Purge_Candidate_With_Open_Application_Even_If_Past_Window()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, oldEnough);
        db.Candidates.Add(candidate);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.PurgedCount);

        var saved = await db.Candidates.SingleAsync();
        Assert.Null(saved.PurgedAt);
    }

    [Fact]
    public async Task HandleAsync_Purges_Candidate_Whose_Only_Application_Is_Withdrawn()
    {
        // Withdrawn applications don't count against eligibility — a withdrawn application on a
        // non-terminal stage should not block purging.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, null, oldEnough);
        application.Withdraw(oldEnough);
        db.Candidates.Add(candidate);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PurgedCount);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Purge_Hired_Candidate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        candidate.LinkToEmployee(Guid.NewGuid(), oldEnough);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.PurgedCount);

        var saved = await db.Candidates.SingleAsync();
        Assert.Null(saved.PurgedAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_RePurge_Or_DoubleCount_Already_Purged_Candidate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        candidate.Purge(Guid.NewGuid(), oldEnough);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.PurgedCount);
    }

    [Fact]
    public async Task HandleAsync_Changing_CandidateRetentionDays_Changes_Eligibility()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        // 100 days old: not eligible under the default 730-day window, but eligible under a 90-day window.
        var candidate = CreateCandidateUpdatedAt(companyId, Now.AddDays(-100));
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var defaultResult = await handler(db).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.Equal(0, defaultResult.Value!.PurgedCount);

        var shortRetentionReader = new FakeCompanyRecruitmentSettingsReader(
            new CompanyRecruitmentSettings(false, false, 90));

        var shortRetentionResult = await handler(db, shortRetentionReader).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(shortRetentionResult.IsSuccess);
        Assert.Equal(1, shortRetentionResult.Value!.PurgedCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_And_Purges_Nothing_When_Company_Is_Under_Legal_Hold()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var result = await handler(db, auditPublisher: auditPublisher, legalHoldStatusReader: new FakeLegalHoldStatusReader(companyId)).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(auditPublisher.Published);

        var saved = await db.Candidates.SingleAsync();
        Assert.Null(saved.PurgedAt);
    }

    [Fact]
    public async Task HandleAsync_Redacts_Application_FreeText_But_Keeps_Structural_Fields()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(
            Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.Interview.Id, "Some pipeline notes", oldEnough);
        application.RecordRejection(stages.Interview.Id, "Not a culture fit", oldEnough);
        application.RecordCvReview("Strong CV", Guid.NewGuid(), oldEnough);
        application.RecordOfferTerms(50000m, OfferSalaryFrequency.Annual, null, DateOnly.FromDateTime(oldEnough.Date), "Confidential offer notes", oldEnough);
        // No open (non-terminal-stage, non-withdrawn) application should exist for the candidate to
        // remain eligible — withdraw so it doesn't block eligibility while still carrying data to redact.
        application.Withdraw(oldEnough);
        db.Candidates.Add(candidate);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PurgedCount);

        var savedApplication = await db.Applications.SingleAsync();
        Assert.Null(savedApplication.Notes);
        Assert.Null(savedApplication.RejectionReason);
        Assert.Null(savedApplication.CvReviewNotes);
        Assert.Null(savedApplication.OfferNotes);

        // Structural/history fields must be left untouched.
        Assert.Equal(stages.Interview.Id, savedApplication.CurrentStageId);
        Assert.Equal(oldEnough, savedApplication.AppliedAt);
        Assert.Equal(50000m, savedApplication.OfferedSalary);
        Assert.Equal(OfferSalaryFrequency.Annual, savedApplication.OfferedSalaryFrequency);
    }

    [Fact]
    public async Task HandleAsync_Purges_Eligible_Candidates_Documents_And_Enqueues_Storage_Deletion_Job_Per_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var candidate = CreateCandidateUpdatedAt(companyId, oldEnough);
        var document1 = CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidate.Id, "CV", "cv.pdf", 2048, "application/pdf",
            $"{companyId}/{candidate.Id}/{Guid.NewGuid():N}/cv.pdf", Guid.NewGuid(), oldEnough);
        var document2 = CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidate.Id, "Cover letter", "cover.pdf", 1024, "application/pdf",
            $"{companyId}/{candidate.Id}/{Guid.NewGuid():N}/cover.pdf", Guid.NewGuid(), oldEnough);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(document1, document2);
        await db.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();

        var result = await handler(db, backgroundJobClient: jobClient).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PurgedCount);

        Assert.Empty(await db.CandidateDocuments.ToListAsync());

        Assert.Equal(2, jobClient.CreatedJobs.Count);
        var enqueuedStorageKeys = jobClient.CreatedJobs
            .Select(job =>
            {
                Assert.Equal(typeof(PurgeCandidateDocumentStorageJob), job.Type);
                Assert.Equal(nameof(PurgeCandidateDocumentStorageJob.ProcessAsync), job.Method.Name);
                return (string)job.Args[0];
            })
            .ToList();
        Assert.Contains(document1.StorageKey, enqueuedStorageKeys);
        Assert.Contains(document2.StorageKey, enqueuedStorageKeys);
    }

    [Fact]
    public async Task HandleAsync_Leaves_NonEligible_Candidates_Applications_And_Documents_Untouched()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddDays(-731);
        var eligibleCandidate = CreateCandidateUpdatedAt(companyId, oldEnough);

        var recentCandidate = CreateCandidateUpdatedAt(companyId, Now.AddDays(-10));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var untouchedApplication = Application.Create(
            Guid.NewGuid(), companyId, vacancy.Id, recentCandidate.Id, stages.Interview.Id, "Keep me", Now.AddDays(-10));
        var untouchedDocument = CandidateDocument.Create(
            Guid.NewGuid(), companyId, recentCandidate.Id, "CV", "cv.pdf", 2048, "application/pdf",
            $"{companyId}/{recentCandidate.Id}/{Guid.NewGuid():N}/cv.pdf", Guid.NewGuid(), Now.AddDays(-10));

        db.Candidates.AddRange(eligibleCandidate, recentCandidate);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(untouchedApplication);
        db.CandidateDocuments.Add(untouchedDocument);
        await db.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();

        var result = await handler(db, backgroundJobClient: jobClient).HandleAsync(
            new PurgeEligibleCandidatesRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PurgedCount);

        var savedApplication = await db.Applications.SingleAsync();
        Assert.Equal("Keep me", savedApplication.Notes);

        var savedDocument = await db.CandidateDocuments.SingleAsync();
        Assert.Equal(untouchedDocument.Id, savedDocument.Id);

        Assert.Empty(jobClient.CreatedJobs);
    }

    private static PurgeEligibleCandidatesHandler handler(
        RecruitmentDbContext db,
        FakeCompanyRecruitmentSettingsReader? recruitmentSettingsReader = null,
        FakeAuditPublisher? auditPublisher = null,
        FakeLegalHoldStatusReader? legalHoldStatusReader = null,
        Infrastructure.RecordingBackgroundJobClient? backgroundJobClient = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher(), recruitmentSettingsReader ?? new FakeCompanyRecruitmentSettingsReader(), legalHoldStatusReader ?? new FakeLegalHoldStatusReader(), backgroundJobClient ?? new Infrastructure.RecordingBackgroundJobClient());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
