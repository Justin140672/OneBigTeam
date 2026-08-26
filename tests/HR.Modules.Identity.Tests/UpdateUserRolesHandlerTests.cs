using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.UpdateUserRoles;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class UpdateUserRolesHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private UpdateUserRolesHandler BuildHandler(FakeAuditEventPublisher auditPublisher, FakeTargetUserCompanyGuard? guard = null) =>
        new(fixture.BuildContext(), Clock, auditPublisher, guard ?? new FakeTargetUserCompanyGuard());

    private async Task<Guid> SeedUser(string suffix)
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(ApplicationUser.Create(userId, $"user{suffix}@test.com", "hash", "Test", "User", Now));
        await db.SaveChangesAsync();
        return userId;
    }

    private async Task<Guid> SeedRole(string name)
    {
        await using var db = fixture.BuildContext();
        var roleId = Guid.NewGuid();
        db.Roles.Add(Role.Create(roleId, name, Now));
        await db.SaveChangesAsync();
        return roleId;
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_User_Missing()
    {
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid(), RoleIds = [Guid.NewGuid()] },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_RoleId_Unknown()
    {
        var userId = await SeedUser("unknown-role");
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [Guid.NewGuid()] },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_RoleIds_Empty()
    {
        var userId = await SeedUser("empty-roles");
        var handler = BuildHandler(new FakeAuditEventPublisher());

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [] },
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Replaces_Role_Set_And_Publishes_Audit_Event_When_Changed()
    {
        var userId = await SeedUser("replace-roles");
        var oldRoleId = await SeedRole("OldRole");
        var newRoleId = await SeedRole("NewRole");

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(userId, oldRoleId, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [newRoleId] },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var db2 = fixture.BuildContext();
        var roles = await db2.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        Assert.Single(roles);
        Assert.Contains(newRoleId, roles);
        Assert.DoesNotContain(oldRoleId, roles);

        Assert.Single(auditPublisher.PublishedEvents, e => e is UserRolesChangedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Role_Set_Unchanged()
    {
        var userId = await SeedUser("unchanged-roles");
        var roleId = await SeedRole("SameRole");

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(userId, roleId, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher);

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [roleId] },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_And_Does_Not_Change_Roles_When_Guard_Reports_Not_A_Member()
    {
        var userId = await SeedUser("cross-tenant");
        var oldRoleId = await SeedRole("OldRoleCrossTenant");
        var newRoleId = await SeedRole("NewRoleCrossTenant");

        await using (var db = fixture.BuildContext())
        {
            db.UserRoles.Add(UserRole.Create(userId, oldRoleId, Now));
            await db.SaveChangesAsync();
        }

        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(auditPublisher, new FakeTargetUserCompanyGuard(isMember: false));

        var result = await handler.HandleAsync(
            new UpdateUserRolesRequest { CompanyId = Guid.NewGuid(), UserId = userId, RoleIds = [newRoleId] },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        await using var db2 = fixture.BuildContext();
        var roles = await db2.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();
        Assert.Single(roles);
        Assert.Contains(oldRoleId, roles); // untouched — guard short-circuited before any read/write

        Assert.Empty(auditPublisher.PublishedEvents);
    }
}
