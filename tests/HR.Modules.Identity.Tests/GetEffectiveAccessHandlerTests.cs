using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.GetEffectiveAccess;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class GetEffectiveAccessHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private GetEffectiveAccessHandler BuildHandler(FakeTargetUserCompanyGuard? guard = null) =>
        new(fixture.BuildContext(), new FakeEmployeeNameReader(), guard ?? new FakeTargetUserCompanyGuard(), new IdentityAuthorizationService(fixture.BuildContext(), Clock), Clock);

    // -----------------------------------------------------------------------
    // 1. Guard short-circuit
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Guard_Reports_Not_A_Member()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var guard = new FakeTargetUserCompanyGuard(isMember: false);
        var handler = BuildHandler(guard);

        var result = await handler.HandleAsync(
            new GetEffectiveAccessRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal((companyId, employeeId), guard.LastCall);
    }

    // -----------------------------------------------------------------------
    // 2. Position + direct role, no overrides
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Populates_Position_DirectRoles_And_InheritedRoles_With_Correct_Sources()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var inheritedRoleId = Guid.NewGuid();
        var directRoleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "position-direct@test.com", "hash", "Test", "User", Now));
            db.Positions.Add(Position.Create(positionId, companyId, "Team Lead", Now));
            db.Roles.Add(Role.Create(inheritedRoleId, "InheritedRole", Now));
            db.Roles.Add(Role.Create(directRoleId, "DirectRole", Now));
            db.PositionRoles.Add(PositionRole.Create(positionId, inheritedRoleId, Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, positionId, Now));
            db.UserRoles.Add(UserRole.Create(employeeId, directRoleId, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeTargetUserCompanyGuard(isMember: true));

        var result = await handler.HandleAsync(
            new GetEffectiveAccessRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value;

        Assert.NotNull(response.Position);
        Assert.Equal(positionId, response.Position!.Id);
        Assert.Equal("Team Lead", response.Position.Name);

        Assert.Single(response.DirectRoles, r => r.Id == directRoleId);
        Assert.Single(response.InheritedRoles, r => r.RoleId == inheritedRoleId && r.PositionId == positionId && r.PositionName == "Team Lead");

        var effectiveDirect = Assert.Single(response.EffectiveRoles, r => r.RoleId == directRoleId);
        Assert.Contains("Direct", effectiveDirect.Sources);

        var effectiveInherited = Assert.Single(response.EffectiveRoles, r => r.RoleId == inheritedRoleId);
        Assert.Contains("Position:Team Lead", effectiveInherited.Sources);

        Assert.Empty(response.Overrides);
        Assert.Empty(response.DeniedPermissions);
    }

    // -----------------------------------------------------------------------
    // 3. Active Deny override
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Active_Deny_Override_Removes_Inherited_Role_And_Populates_DeniedPermissions()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var deniedRoleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        Guid overrideId;

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "deny-override@test.com", "hash", "Test", "User", Now));
            db.Positions.Add(Position.Create(positionId, companyId, "Denied Position", Now));
            db.Roles.Add(Role.Create(deniedRoleId, "RoleToDeny", Now));
            db.PositionRoles.Add(PositionRole.Create(positionId, deniedRoleId, Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, positionId, Now));
            db.Permissions.Add(Permission.Create(permissionId, "denied.permission", Now));
            db.RolePermissions.Add(RolePermission.Create(deniedRoleId, permissionId));

            var @override = EmployeeRoleOverride.Create(companyId, employeeId, deniedRoleId, EmployeeRoleOverrideType.Deny, "No longer needed", null, Now);
            overrideId = @override.Id;
            db.EmployeeRoleOverrides.Add(@override);

            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeTargetUserCompanyGuard(isMember: true));

        var result = await handler.HandleAsync(
            new GetEffectiveAccessRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value;

        Assert.DoesNotContain(response.EffectiveRoles, r => r.RoleId == deniedRoleId);
        Assert.DoesNotContain(response.EffectivePermissions, p => p.PermissionId == permissionId);

        var denied = Assert.Single(response.DeniedPermissions, p => p.PermissionId == permissionId);
        Assert.Equal(deniedRoleId, denied.DeniedByRoleId);
        Assert.Equal("RoleToDeny", denied.DeniedByRoleName);
        Assert.Equal(overrideId, denied.OverrideId);
        Assert.Equal("No longer needed", denied.Reason);

        var overrideDto = Assert.Single(response.Overrides, o => o.Id == overrideId);
        Assert.True(overrideDto.IsActive);
    }

    // -----------------------------------------------------------------------
    // 4. Active Grant override
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Active_Grant_Override_Adds_Role_With_Override_Source_And_Its_Permissions()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var grantedRoleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "grant-override@test.com", "hash", "Test", "User", Now));
            db.Roles.Add(Role.Create(grantedRoleId, "GrantedRole", Now));
            db.Permissions.Add(Permission.Create(permissionId, "granted.permission", Now));
            db.RolePermissions.Add(RolePermission.Create(grantedRoleId, permissionId));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, grantedRoleId, EmployeeRoleOverrideType.Grant, "Temporary cover", null, Now));
            await db.SaveChangesAsync();
        }

        var handler = BuildHandler(new FakeTargetUserCompanyGuard(isMember: true));

        var result = await handler.HandleAsync(
            new GetEffectiveAccessRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value;

        var effectiveGranted = Assert.Single(response.EffectiveRoles, r => r.RoleId == grantedRoleId);
        Assert.Contains("Override", effectiveGranted.Sources);

        Assert.Contains(response.EffectivePermissions, p => p.PermissionId == permissionId);
    }

    // -----------------------------------------------------------------------
    // 5. Expired override
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_Expired_Override_Does_Not_Affect_Effective_Access_But_Is_Listed_As_Inactive()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var grantedRoleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "expired-override@test.com", "hash", "Test", "User", Now));
            db.Roles.Add(Role.Create(grantedRoleId, "ExpiredGrantRole", Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, grantedRoleId, EmployeeRoleOverrideType.Grant, "Expired", Now.AddSeconds(-1), Now));
            await db.SaveChangesAsync();
        }

        try
        {
            var handler = BuildHandler(new FakeTargetUserCompanyGuard(isMember: true));

            var result = await handler.HandleAsync(
                new GetEffectiveAccessRequest(companyId, employeeId),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            var response = result.Value;

            Assert.DoesNotContain(response.EffectiveRoles, r => r.RoleId == grantedRoleId);

            var overrideDto = Assert.Single(response.Overrides, o => o.RoleId == grantedRoleId);
            Assert.False(overrideDto.IsActive);
        }
        finally
        {
            // This test deliberately seeds an already-expired override to exercise the
            // "IsActive == false" display path. The shared "IdentityDatabase" fixture is not
            // scoped per test, and ExpireEmployeeRoleOverridesJobTests scans the whole
            // employee_role_overrides table unfiltered — leaving an expired row behind here
            // would make that job's "no-op when nothing is expired" assertion flaky depending
            // on test execution order. Clean up explicitly rather than leaving it for the
            // recurring job to sweep.
            await using var cleanup = fixture.BuildContext();
            var toRemove = await cleanup.EmployeeRoleOverrides
                .Where(o => o.UserId == employeeId && o.RoleId == grantedRoleId)
                .ToListAsync();
            cleanup.EmployeeRoleOverrides.RemoveRange(toRemove);
            await cleanup.SaveChangesAsync();
        }
    }

    // -----------------------------------------------------------------------
    // 6. Critical cross-check against the real IdentityAuthorizationService
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_EffectiveRoles_And_EffectivePermissions_Exactly_Match_AuthorizationService()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        var directRoleId = Guid.NewGuid();
        var inheritedRoleId = Guid.NewGuid();
        var deniedInheritedRoleId = Guid.NewGuid();
        var grantedExtraRoleId = Guid.NewGuid();

        var directPermissionId = Guid.NewGuid();
        var inheritedPermissionId = Guid.NewGuid();
        var deniedPermissionId = Guid.NewGuid();
        var grantedPermissionId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(employeeId, "cross-check@test.com", "hash", "Test", "User", Now));
            db.Positions.Add(Position.Create(positionId, companyId, "Cross Check Position", Now));

            db.Roles.Add(Role.Create(directRoleId, "CrossDirectRole", Now));
            db.Roles.Add(Role.Create(inheritedRoleId, "CrossInheritedRole", Now));
            db.Roles.Add(Role.Create(deniedInheritedRoleId, "CrossDeniedInheritedRole", Now));
            db.Roles.Add(Role.Create(grantedExtraRoleId, "CrossGrantedExtraRole", Now));

            db.PositionRoles.Add(PositionRole.Create(positionId, inheritedRoleId, Now));
            db.PositionRoles.Add(PositionRole.Create(positionId, deniedInheritedRoleId, Now));
            db.UserPositions.Add(UserPosition.Create(employeeId, positionId, Now));
            db.UserRoles.Add(UserRole.Create(employeeId, directRoleId, Now));

            db.Permissions.Add(Permission.Create(directPermissionId, "cross.direct", Now));
            db.Permissions.Add(Permission.Create(inheritedPermissionId, "cross.inherited", Now));
            db.Permissions.Add(Permission.Create(deniedPermissionId, "cross.denied", Now));
            db.Permissions.Add(Permission.Create(grantedPermissionId, "cross.granted", Now));

            db.RolePermissions.Add(RolePermission.Create(directRoleId, directPermissionId));
            db.RolePermissions.Add(RolePermission.Create(inheritedRoleId, inheritedPermissionId));
            db.RolePermissions.Add(RolePermission.Create(deniedInheritedRoleId, deniedPermissionId));
            db.RolePermissions.Add(RolePermission.Create(grantedExtraRoleId, grantedPermissionId));

            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, deniedInheritedRoleId, EmployeeRoleOverrideType.Deny, "Cross-check deny", null, Now));
            db.EmployeeRoleOverrides.Add(
                EmployeeRoleOverride.Create(companyId, employeeId, grantedExtraRoleId, EmployeeRoleOverrideType.Grant, "Cross-check grant", null, Now));

            await db.SaveChangesAsync();
        }

        var authorizationService = new IdentityAuthorizationService(fixture.BuildContext(), Clock);
        var handler = new GetEffectiveAccessHandler(
            fixture.BuildContext(),
            new FakeEmployeeNameReader(),
            new FakeTargetUserCompanyGuard(isMember: true),
            authorizationService,
            Clock);

        var result = await handler.HandleAsync(
            new GetEffectiveAccessRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value;

        var expectedRoles = await authorizationService.GetEffectiveRolesAsync(employeeId);
        var expectedPermissions = await authorizationService.GetEffectivePermissionsAsync(employeeId);

        Assert.Equal(expectedRoles, response.EffectiveRoles.Select(r => r.RoleId).ToHashSet());
        Assert.Equal(expectedPermissions, response.EffectivePermissions.Select(p => p.PermissionId).ToHashSet());
    }
}
