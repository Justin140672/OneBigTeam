using HR.Modules.Recruitment.Features.CreateExternalRecruiter;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class CreateExternalRecruiterHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Creates_Recruiter_As_Active()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new CreateExternalRecruiterRequest(companyId, "Acme Recruiting", "Jane Smith", "jane@acme.com", "01234", "https://acme.com", "Notes"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsActive);
        Assert.Equal("Acme Recruiting", result.Value.AgencyName);

        var saved = await db.ExternalRecruiters.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Allows_Duplicate_Agency_Names()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        await handler(db).HandleAsync(
            new CreateExternalRecruiterRequest(companyId, "Acme Recruiting", null, null, null, null, null),
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateExternalRecruiterRequest(companyId, "Acme Recruiting", null, null, null, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await db.ExternalRecruiters.CountAsync());
    }

    [Fact]
    public async Task HandleAsync_Publishes_ExternalRecruiterCreatedAuditEvent()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();
        var companyId = Guid.NewGuid();

        var result = await new CreateExternalRecruiterHandler(db, new FakeClock(FixedUtcNow), auditPublisher).HandleAsync(
            new CreateExternalRecruiterRequest(companyId, "Acme Recruiting", null, null, null, null, null),
            CancellationToken.None);

        var evt = Assert.Single(auditPublisher.Published);
        Assert.Equal("external_recruiter.created", evt.EventType);
        Assert.Equal(result.Value!.Id, evt.EntityId);
    }

    private static CreateExternalRecruiterHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
