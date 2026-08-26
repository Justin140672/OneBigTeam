using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class ExpireEmployeeRoleOverridesJobTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private ExpireEmployeeRoleOverridesJob BuildJob(FakeAuditEventPublisher auditPublisher) =>
        new(fixture.BuildContext(), Clock, auditPublisher);

    private async Task<Guid> SeedUser(string suffix)
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(ApplicationUser.Create(userId, $"user{suffix}@test.com", "hash", "Test", "User", Now));
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task<Guid> SeedOverride(
        Guid companyId, Guid userId, Guid roleId, DateTimeOffset? expiresAt, EmployeeRoleOverrideType type = EmployeeRoleOverrideType.Grant)
    {
        await using var db = fixture.BuildContext();
        var @override = EmployeeRoleOverride.Create(companyId, userId, roleId, type, "Reason", expiresAt, Now.AddDays(-1));
        db.EmployeeRoleOverrides.Add(@override);
        await db.SaveChangesAsync();
        return @override.Id;
    }

    [Fact]
    public async Task ExecuteAsync_Is_A_NoOp_When_Nothing_Is_Expired()
    {
        var userId = await SeedUser("nothing-expired");
        var companyId = Guid.NewGuid();
        var permanentId = await SeedOverride(companyId, userId, SystemRoles.Manager, expiresAt: null);
        var futureId = await SeedOverride(companyId, userId, SystemRoles.Recruiter, expiresAt: Now.AddDays(1));

        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(auditPublisher);

        await job.ExecuteAsync();

        Assert.Empty(auditPublisher.PublishedEvents);

        await using var db = fixture.BuildContext();
        Assert.True(await db.EmployeeRoleOverrides.AnyAsync(o => o.Id == permanentId));
        Assert.True(await db.EmployeeRoleOverrides.AnyAsync(o => o.Id == futureId));
    }

    [Fact]
    public async Task ExecuteAsync_Removes_Only_Overrides_Whose_ExpiresAt_Has_Passed_Or_Is_Now()
    {
        var userId = await SeedUser("mixed-expiry");
        var companyId = Guid.NewGuid();
        var expiredId = await SeedOverride(companyId, userId, SystemRoles.Manager, expiresAt: Now.AddDays(-1));
        var expiringNowId = await SeedOverride(companyId, userId, SystemRoles.Recruiter, expiresAt: Now); // boundary: ExpiresAt == now
        var permanentId = await SeedOverride(companyId, userId, SystemRoles.Employee, expiresAt: null);
        var futureId = await SeedOverride(companyId, userId, SystemRoles.HrAdministrator, expiresAt: Now.AddDays(1));

        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(auditPublisher);

        await job.ExecuteAsync();

        await using var db = fixture.BuildContext();
        Assert.False(await db.EmployeeRoleOverrides.AnyAsync(o => o.Id == expiredId));
        Assert.False(await db.EmployeeRoleOverrides.AnyAsync(o => o.Id == expiringNowId));
        Assert.True(await db.EmployeeRoleOverrides.AnyAsync(o => o.Id == permanentId));
        Assert.True(await db.EmployeeRoleOverrides.AnyAsync(o => o.Id == futureId));

        Assert.Equal(2, auditPublisher.PublishedEvents.Count(e => e is EmployeeRoleOverrideExpiredAuditEvent));
        Assert.Single(auditPublisher.PublishedEvents, e =>
            e is EmployeeRoleOverrideExpiredAuditEvent expired &&
            expired.OverrideId == expiredId &&
            expired.RoleId == SystemRoles.Manager &&
            expired.CompanyId == companyId &&
            expired.UserId == userId);
        Assert.Single(auditPublisher.PublishedEvents, e =>
            e is EmployeeRoleOverrideExpiredAuditEvent expired && expired.OverrideId == expiringNowId);
    }
}
