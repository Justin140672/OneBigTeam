using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.RemoveEmployeeRoleOverride;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class RemoveEmployeeRoleOverrideHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private RemoveEmployeeRoleOverrideHandler BuildHandler(
        FakeAuditEventPublisher auditPublisher,
        FakeTargetUserCompanyGuard? guard = null) =>
        new(
            fixture.BuildContext(),
            Clock,
            auditPublisher,
            guard ?? new FakeTargetUserCompanyGuard(),
            new IdentityAuthorizationService(fixture.BuildContext(), Clock));

    private async Task<Guid> SeedUser(string suffix)
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(ApplicationUser.Create(userId, $"user-{suffix}-{userId:N}@test.com", "hash", "Test", "User", Now));
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task GrantSystemRole(Guid userId, Guid systemRoleId)
    {
        await using var db = fixture.BuildContext();
        if (!await db.Roles.AnyAsync(r => r.Id == systemRoleId))
            db.Roles.Add(Role.Create(systemRoleId, systemRoleId.ToString(), Now));
        db.UserRoles.Add(UserRole.Create(userId, systemRoleId, Now));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedOverride(Guid companyId, Guid userId, Guid roleId, EmployeeRoleOverrideType type = EmployeeRoleOverrideType.Grant)
    {
        await using var db = fixture.BuildContext();
        var @override = EmployeeRoleOverride.Create(companyId, userId, roleId, type, "Reason", null, Now);
        db.EmployeeRoleOverrides.Add(@override);
        await db.SaveChangesAsync();
        return @override.Id;
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Target_User_Not_A_Company_Member()
    {
        var targetUserId = await SeedUser("wrong-company");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);
        var companyId = Guid.NewGuid();
        await SeedOverride(companyId, targetUserId, SystemRoles.Manager);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, new FakeTargetUserCompanyGuard(isMember: false));

        var result = await handler.HandleAsync(
            new RemoveEmployeeRoleOverrideRequest { CompanyId = companyId, UserId = targetUserId, RoleId = SystemRoles.Manager },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Override_Exists_For_That_Role()
    {
        var targetUserId = await SeedUser("no-override");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new RemoveEmployeeRoleOverrideRequest { CompanyId = Guid.NewGuid(), UserId = targetUserId, RoleId = SystemRoles.Manager },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Role_Outside_Actors_Administrable_Set()
    {
        var targetUserId = await SeedUser("unauthorised-target");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);
        var companyId = Guid.NewGuid();
        await SeedOverride(companyId, targetUserId, SystemRoles.CompanyAdministrator);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new RemoveEmployeeRoleOverrideRequest { CompanyId = companyId, UserId = targetUserId, RoleId = SystemRoles.CompanyAdministrator },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);

        await using var db = fixture.BuildContext();
        Assert.True(await db.EmployeeRoleOverrides.AnyAsync(o => o.UserId == targetUserId)); // untouched
    }

    [Fact]
    public async Task HandleAsync_Removes_Override_And_Publishes_Audit_Event_On_Happy_Path()
    {
        var targetUserId = await SeedUser("happy-path");
        var actorId = Guid.NewGuid();
        await GrantSystemRole(actorId, SystemRoles.HrAdministrator);
        var companyId = Guid.NewGuid();
        var overrideId = await SeedOverride(companyId, targetUserId, SystemRoles.Manager, EmployeeRoleOverrideType.Deny);

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new RemoveEmployeeRoleOverrideRequest { CompanyId = companyId, UserId = targetUserId, RoleId = SystemRoles.Manager },
            actorUserId: actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db = fixture.BuildContext();
        Assert.False(await db.EmployeeRoleOverrides.AnyAsync(o => o.Id == overrideId));

        Assert.Single(auditPublisher.PublishedEvents, e =>
            e is EmployeeRoleOverrideRemovedAuditEvent removed &&
            removed.CompanyId == companyId &&
            removed.UserId == targetUserId &&
            removed.RoleId == SystemRoles.Manager &&
            removed.OverrideType == EmployeeRoleOverrideType.Deny &&
            removed.ActorUserId == actorId);
    }
}
