using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.PurgeEligibleCandidates;
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

    private static PurgeEligibleCandidatesHandler handler(
        RecruitmentDbContext db,
        FakeCompanyRecruitmentSettingsReader? recruitmentSettingsReader = null,
        FakeAuditPublisher? auditPublisher = null,
        FakeLegalHoldStatusReader? legalHoldStatusReader = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher(), recruitmentSettingsReader ?? new FakeCompanyRecruitmentSettingsReader(), legalHoldStatusReader ?? new FakeLegalHoldStatusReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
