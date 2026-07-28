using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateExternalRecruiter;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class UpdateExternalRecruiterHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Updates_Recruiter_Details()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new UpdateExternalRecruiterRequest(companyId, recruiter.Id, "New Agency Name", "John Doe", "john@newagency.com", "9999", "https://newagency.com", "Updated"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Agency Name", result.Value!.AgencyName);
        var saved = await db.ExternalRecruiters.SingleAsync();
        Assert.Equal("New Agency Name", saved.AgencyName);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Recruiter_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new UpdateExternalRecruiterRequest(Guid.NewGuid(), Guid.NewGuid(), "New Name", null, null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Recruiter_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), Guid.NewGuid(), "Acme Recruiting", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new UpdateExternalRecruiterRequest(Guid.NewGuid(), recruiter.Id, "New Name", null, null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_ExternalRecruiterUpdatedAuditEvent_With_Before_And_After()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();
        var companyId = Guid.NewGuid();
        var recruiter = ExternalRecruiter.Create(Guid.NewGuid(), companyId, "Old Name", null, null, null, null, null, Now);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();

        await new UpdateExternalRecruiterHandler(db, new FakeClock(FixedUtcNow), auditPublisher).HandleAsync(
            new UpdateExternalRecruiterRequest(companyId, recruiter.Id, "New Name", null, null, null, null, null),
            CancellationToken.None);

        var evt = Assert.Single(auditPublisher.Published);
        Assert.Equal("external_recruiter.updated", evt.EventType);
    }

    private static UpdateExternalRecruiterHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
