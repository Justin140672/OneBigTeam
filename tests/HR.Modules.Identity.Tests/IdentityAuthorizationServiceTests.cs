using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class IdentityAuthorizationServiceTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private IdentityAuthorizationService BuildService() =>
        new(fixture.BuildContext(), Clock);

    private async Task<(Guid userId, Guid roleId)> SeedUserWithDirectRole(string suffix = "")
    {
        await using var db = fixture.BuildContext();

        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        db.Users.Add(ApplicationUser.Create(userId, $"user{suffix}@test.com", "hash", "Test", "User", Now));
        db.Roles.Add(Role.Create(roleId, $"Role{suffix}", Now));
        db.UserRoles.Add(UserRole.Create(userId, roleId, Now));

        await db.SaveChangesAsync();
        return (userId, roleId);
    }

    private async Task<(Guid userId, Guid positionRoleId)> SeedUserWithPositionRole(
        DateTimeOffset? positionExpiresAt = null,
        string suffix = "")
    {
        await using var db = fixture.BuildContext();

        var userId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        db.Users.Add(ApplicationUser.Create(userId, $"pos{suffix}@test.com", "hash", "Test", "User", Now));
        db.Positions.Add(Position.Create(positionId, "tenant-1", $"Position{suffix}", Now));
        db.Roles.Add(Role.Create(roleId, $"PosRole{suffix}", Now));
        db.PositionRoles.Add(PositionRole.Create(positionId, roleId, Now));
        db.UserPositions.Add(UserPosition.Create(userId, positionId, Now, positionExpiresAt));

        await db.SaveChangesAsync();
        return (userId, roleId);
    }

    private async Task<Guid> SeedPermissionForRole(Guid roleId)
    {
        await using var db = fixture.BuildContext();

        var permissionId = Guid.NewGuid();
        db.Permissions.Add(Permission.Create(permissionId, $"perm.{permissionId:N}", Now));
        db.RolePermissions.Add(RolePermission.Create(roleId, permissionId));

        await db.SaveChangesAsync();
        return permissionId;
    }

    // -----------------------------------------------------------------------
    // GetEffectiveRolesAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetEffectiveRoles_Returns_Empty_For_Unknown_User()
    {
        var svc = BuildService();

        var roles = await svc.GetEffectiveRolesAsync(Guid.NewGuid());

        Assert.Empty(roles);
    }

    [Fact]
    public async Task GetEffectiveRoles_Includes_Direct_UserRole()
    {
        var (userId, roleId) = await SeedUserWithDirectRole("direct");

        var svc = BuildService();
        var roles = await svc.GetEffectiveRolesAsync(userId);

        Assert.Contains(roleId, roles);
    }

    [Fact]
    public async Task GetEffectiveRoles_Includes_Position_Inherited_Role()
    {
        var (userId, roleId) = await SeedUserWithPositionRole(suffix: "pos-active");

        var svc = BuildService();
        var roles = await svc.GetEffectiveRolesAsync(userId);

        Assert.Contains(roleId, roles);
    }

    [Fact]
    public async Task GetEffectiveRoles_Excludes_Expired_Position_Role()
    {
        // Position expired 1 second before "now".
        var (userId, roleId) = await SeedUserWithPositionRole(
            positionExpiresAt: Now.AddSeconds(-1),
            suffix: "pos-expired");

        var svc = BuildService();
        var roles = await svc.GetEffectiveRolesAsync(userId);

        Assert.DoesNotContain(roleId, roles);
    }

    [Fact]
    public async Task GetEffectiveRoles_Excludes_Position_Role_Expiring_Exactly_At_Now()
    {
        // ExpiresAt == now is excluded because the comparison is strictly ">" not ">=".
        var (userId, roleId) = await SeedUserWithPositionRole(
            positionExpiresAt: Now,
            suffix: "pos-exact-now");

        var svc = BuildService();
        var roles = await svc.GetEffectiveRolesAsync(userId);

        Assert.DoesNotContain(roleId, roles);
    }

    [Fact]
    public async Task GetEffectiveRoles_Includes_Position_Role_Expiring_One_Second_After_Now()
    {
        var (userId, roleId) = await SeedUserWithPositionRole(
            positionExpiresAt: Now.AddSeconds(1),
            suffix: "pos-just-active");

        var svc = BuildService();
        var roles = await svc.GetEffectiveRolesAsync(userId);

        Assert.Contains(roleId, roles);
    }

    [Fact]
    public async Task GetEffectiveRoles_Deny_Override_Removes_Position_Inherited_Role()
    {
        var (userId, roleId) = await SeedUserWithPositionRole(suffix: "deny-pos");

        await using var db = fixture.BuildContext();
        db.EmployeeRoleOverrides.Add(
            EmployeeRoleOverride.Create(userId, roleId, EmployeeRoleOverrideType.Deny, Now));
        await db.SaveChangesAsync();

        var svc = BuildService();
        var roles = await svc.GetEffectiveRolesAsync(userId);

        Assert.DoesNotContain(roleId, roles);
    }

    [Fact]
    public async Task GetEffectiveRoles_Grant_Override_Adds_Role_Beyond_Position()
    {
        var (userId, _) = await SeedUserWithPositionRole(suffix: "grant-base");

        await using var db = fixture.BuildContext();
        var extraRoleId = Guid.NewGuid();
        db.Roles.Add(Role.Create(extraRoleId, "ExtraGrantRole", Now));
        db.EmployeeRoleOverrides.Add(
            EmployeeRoleOverride.Create(userId, extraRoleId, EmployeeRoleOverrideType.Grant, Now));
        await db.SaveChangesAsync();

        var svc = BuildService();
        var roles = await svc.GetEffectiveRolesAsync(userId);

        Assert.Contains(extraRoleId, roles);
    }

    [Fact]
    public async Task GetEffectiveRoles_Deny_Override_Removes_Direct_UserRole()
    {
        var (userId, roleId) = await SeedUserWithDirectRole("deny-direct");

        await using var db = fixture.BuildContext();
        db.EmployeeRoleOverrides.Add(
            EmployeeRoleOverride.Create(userId, roleId, EmployeeRoleOverrideType.Deny, Now));
        await db.SaveChangesAsync();

        var svc = BuildService();
        var roles = await svc.GetEffectiveRolesAsync(userId);

        Assert.DoesNotContain(roleId, roles);
    }

    // -----------------------------------------------------------------------
    // GetEffectivePermissionsAsync / HasPermissionAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetEffectivePermissions_Returns_Permissions_For_Effective_Roles()
    {
        var (userId, roleId) = await SeedUserWithDirectRole("perm-test");
        var permissionId = await SeedPermissionForRole(roleId);

        var svc = BuildService();
        var permissions = await svc.GetEffectivePermissionsAsync(userId);

        Assert.Contains(permissionId, permissions);
    }

    [Fact]
    public async Task HasPermission_Returns_True_When_Permission_Held()
    {
        var (userId, roleId) = await SeedUserWithDirectRole("has-perm");
        var permissionId = await SeedPermissionForRole(roleId);

        var svc = BuildService();

        Assert.True(await svc.HasPermissionAsync(userId, permissionId));
    }

    [Fact]
    public async Task HasPermission_Returns_False_When_Role_Denied()
    {
        var (userId, roleId) = await SeedUserWithDirectRole("denied-perm");
        var permissionId = await SeedPermissionForRole(roleId);

        await using var db = fixture.BuildContext();
        db.EmployeeRoleOverrides.Add(
            EmployeeRoleOverride.Create(userId, roleId, EmployeeRoleOverrideType.Deny, Now));
        await db.SaveChangesAsync();

        var svc = BuildService();

        Assert.False(await svc.HasPermissionAsync(userId, permissionId));
    }

    [Fact]
    public async Task HasPermission_Returns_False_For_Unknown_Permission()
    {
        var (userId, _) = await SeedUserWithDirectRole("unknown-perm");
        var svc = BuildService();

        Assert.False(await svc.HasPermissionAsync(userId, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetEffectivePermissions_Returns_Empty_For_User_With_No_Roles()
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        db.Users.Add(ApplicationUser.Create(userId, "noroles@test.com", "hash", "No", "Roles", Now));
        await db.SaveChangesAsync();

        var svc = BuildService();
        var permissions = await svc.GetEffectivePermissionsAsync(userId);

        Assert.Empty(permissions);
    }

    [Fact]
    public async Task GetEffectivePermissions_Unions_Permissions_Across_Multiple_Roles()
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        var permA = Guid.NewGuid();
        var permB = Guid.NewGuid();

        db.Users.Add(ApplicationUser.Create(userId, "multirole@test.com", "hash", "Multi", "Role", Now));
        db.Roles.Add(Role.Create(roleA, "MultiRoleA", Now));
        db.Roles.Add(Role.Create(roleB, "MultiRoleB", Now));
        db.UserRoles.Add(UserRole.Create(userId, roleA, Now));
        db.UserRoles.Add(UserRole.Create(userId, roleB, Now));
        db.Permissions.Add(Permission.Create(permA, "perm.multi.a", Now));
        db.Permissions.Add(Permission.Create(permB, "perm.multi.b", Now));
        db.RolePermissions.Add(RolePermission.Create(roleA, permA));
        db.RolePermissions.Add(RolePermission.Create(roleB, permB));
        await db.SaveChangesAsync();

        var svc = BuildService();
        var permissions = await svc.GetEffectivePermissionsAsync(userId);

        Assert.Contains(permA, permissions);
        Assert.Contains(permB, permissions);
    }

    [Fact]
    public async Task GetEffectivePermissions_Deduplicates_Permission_Shared_By_Multiple_Roles()
    {
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        var sharedPerm = Guid.NewGuid();

        db.Users.Add(ApplicationUser.Create(userId, "dedupe@test.com", "hash", "De", "Dupe", Now));
        db.Roles.Add(Role.Create(roleA, "DedupeRoleA", Now));
        db.Roles.Add(Role.Create(roleB, "DedupeRoleB", Now));
        db.UserRoles.Add(UserRole.Create(userId, roleA, Now));
        db.UserRoles.Add(UserRole.Create(userId, roleB, Now));
        db.Permissions.Add(Permission.Create(sharedPerm, "perm.shared", Now));
        db.RolePermissions.Add(RolePermission.Create(roleA, sharedPerm));
        db.RolePermissions.Add(RolePermission.Create(roleB, sharedPerm));
        await db.SaveChangesAsync();

        var svc = BuildService();
        var permissions = await svc.GetEffectivePermissionsAsync(userId);

        Assert.Single(permissions, p => p == sharedPerm);
    }

    [Fact]
    public async Task GetEffectivePermissions_Includes_Permissions_From_Position_Inherited_Role()
    {
        var (userId, roleId) = await SeedUserWithPositionRole(suffix: "pos-perm");
        var permissionId = await SeedPermissionForRole(roleId);

        var svc = BuildService();
        var permissions = await svc.GetEffectivePermissionsAsync(userId);

        Assert.Contains(permissionId, permissions);
    }

    [Fact]
    public async Task HasPermission_Remains_True_When_One_Of_Two_Granting_Roles_Is_Denied()
    {
        // Both roleA and roleB grant the same permission.
        // Denying roleA should NOT revoke the permission because roleB still grants it.
        await using var db = fixture.BuildContext();
        var userId = Guid.NewGuid();
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        var permId = Guid.NewGuid();

        db.Users.Add(ApplicationUser.Create(userId, "partial-deny@test.com", "hash", "Partial", "Deny", Now));
        db.Roles.Add(Role.Create(roleA, "PartialDenyRoleA", Now));
        db.Roles.Add(Role.Create(roleB, "PartialDenyRoleB", Now));
        db.UserRoles.Add(UserRole.Create(userId, roleA, Now));
        db.UserRoles.Add(UserRole.Create(userId, roleB, Now));
        db.Permissions.Add(Permission.Create(permId, "perm.partial.deny", Now));
        db.RolePermissions.Add(RolePermission.Create(roleA, permId));
        db.RolePermissions.Add(RolePermission.Create(roleB, permId));
        db.EmployeeRoleOverrides.Add(
            EmployeeRoleOverride.Create(userId, roleA, EmployeeRoleOverrideType.Deny, Now));
        await db.SaveChangesAsync();

        var svc = BuildService();

        Assert.True(await svc.HasPermissionAsync(userId, permId));
    }
}
