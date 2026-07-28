using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.SetExternalRecruiterActiveStatus;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class SetExternalRecruiterActiveStatusHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Deactivates_Recruiter_And_Does_Not_Delete_Row()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new SetExternalRecruiterActiveStatusRequest(companyId, recruiter.Id, false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);

        var saved = await db.ExternalRecruiters.SingleAsync();
        Assert.Equal(recruiter.Id, saved.Id);
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Reactivates_Recruiter()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        recruiter.SetActiveStatus(false, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new SetExternalRecruiterActiveStatusRequest(companyId, recruiter.Id, true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Recruiter_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new SetExternalRecruiterActiveStatusRequest(Guid.NewGuid(), Guid.NewGuid(), false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_ExternalRecruiterActiveStatusChangedAuditEvent()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        await new SetExternalRecruiterActiveStatusHandler(db, new FakeClock(FixedUtcNow), auditPublisher).HandleAsync(
            new SetExternalRecruiterActiveStatusRequest(companyId, recruiter.Id, false),
            CancellationToken.None);

        var evt = Assert.Single(auditPublisher.Published);
        Assert.Equal("external_recruiter.active_status_changed", evt.EventType);
    }

    private static SetExternalRecruiterActiveStatusHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
