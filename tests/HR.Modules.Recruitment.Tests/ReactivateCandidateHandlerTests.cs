using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ReactivateCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ReactivateCandidateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Candidate_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new ReactivateCandidateRequest(Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Candidate_Already_Active()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new ReactivateCandidateRequest(companyId, candidate.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Reactivates_Candidate_And_Leaves_Prior_Deactivation_Fields_Intact()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var deactivatedBy = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        candidate.Deactivate(deactivatedBy, "No longer available", Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var performedBy = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new ReactivateCandidateRequest(companyId, candidate.Id),
            performedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsActive);
        Assert.Equal(performedBy, result.Value.ReactivatedByUserId);

        var saved = await db.Candidates.SingleAsync();
        Assert.True(saved.IsActive);
        Assert.Equal(deactivatedBy, saved.DeactivatedByUserId);
        Assert.Equal("No longer available", saved.DeactivationReason);
        Assert.Equal(Now, saved.DeactivatedAt);
    }

    [Fact]
    public async Task HandleAsync_Publishes_CandidateReactivatedAuditEvent()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        candidate.Deactivate(Guid.NewGuid(), "No longer available", Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var performedBy = Guid.NewGuid();

        await new ReactivateCandidateHandler(db, new FakeClock(FixedUtcNow), auditPublisher).HandleAsync(
            new ReactivateCandidateRequest(companyId, candidate.Id),
            performedBy,
            CancellationToken.None);

        var auditEvent = Assert.IsType<CandidateReactivatedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal("candidate.reactivated", ((IAuditEvent)auditEvent).EventType);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(candidate.Id, auditEvent.CandidateId);
        Assert.Equal(performedBy, auditEvent.ReactivatedByUserId);
    }

    private static ReactivateCandidateHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
