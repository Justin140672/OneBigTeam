using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DeactivateCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class DeactivateCandidateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Candidate_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new DeactivateCandidateRequest(Guid.NewGuid(), Guid.NewGuid(), "No longer available"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Candidate_Already_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        candidate.Deactivate(Guid.NewGuid(), "Already gone", Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new DeactivateCandidateRequest(companyId, candidate.Id, "No longer available"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Candidate_Has_Active_Application()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.CvReview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new DeactivateCandidateRequest(companyId, candidate.Id, "No longer available"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        var saved = await db.Candidates.SingleAsync();
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Only_Applications_Are_Withdrawn_Or_On_Terminal_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyA = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var vacancyB = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Frontend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var withdrawnApplication = Application.Create(Guid.NewGuid(), companyId, vacancyA.Id, candidate.Id, stages.CvReview.Id, null, Now);
        withdrawnApplication.Withdraw(Now);
        var terminalApplication = Application.Create(Guid.NewGuid(), companyId, vacancyB.Id, candidate.Id, stages.Rejected.Id, null, Now);
        db.Vacancies.AddRange(vacancyA, vacancyB);
        db.Candidates.Add(candidate);
        db.Applications.AddRange(withdrawnApplication, terminalApplication);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new DeactivateCandidateRequest(companyId, candidate.Id, "No longer available"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_Candidate_And_Persists_Flag()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var performedBy = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new DeactivateCandidateRequest(companyId, candidate.Id, "No longer available"),
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
        Assert.Equal(performedBy, result.Value.DeactivatedByUserId);
        Assert.Equal("No longer available", result.Value.DeactivationReason);

        var saved = await db.Candidates.SingleAsync();
        Assert.False(saved.IsActive);
        Assert.Equal(performedBy, saved.DeactivatedByUserId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_CandidateDeactivatedAuditEvent_With_Actor_And_Reason()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var performedBy = Guid.NewGuid();

        await new DeactivateCandidateHandler(db, new FakeClock(FixedUtcNow), auditPublisher).HandleAsync(
            new DeactivateCandidateRequest(companyId, candidate.Id, "No longer available"),
            performedBy,
            CancellationToken.None);

        var auditEvent = Assert.IsType<CandidateDeactivatedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal("candidate.deactivated", ((IAuditEvent)auditEvent).EventType);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(candidate.Id, auditEvent.CandidateId);
        Assert.Equal("No longer available", auditEvent.Reason);
        Assert.Equal(performedBy, auditEvent.DeactivatedByUserId);
    }

    private static DeactivateCandidateHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
