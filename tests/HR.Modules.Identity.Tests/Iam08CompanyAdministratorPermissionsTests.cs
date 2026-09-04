using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-08 — a "Company Administrator only" account must not inherit employee visibility.
/// Covers the reconciliation migration's DELETE (idempotency + correctness), the effective-access
/// override semantics that back api/me, and end-to-end role removal.
///
/// OBT-IAM-09: the allow-list below was further narrowed — onboarding:view, onboarding:manage and
/// support:manage were removed from Company Administrator (see RolePermissionConfiguration and
/// migration OBT_IAM09_RemoveCompanyAdministratorOnboardingSupport). Company Administrator is now
/// limited to company:read, company:edit and subscription:manage; a company creator retains
/// onboarding/support access via the HR Administrator role also assigned at signup.
/// </summary>
[Collection("IdentityDatabase")]
public class Iam08CompanyAdministratorPermissionsTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    // Kept in sync with
    // src/Modules/HR.Modules.Identity/Migrations/20260903114519_IAM08_ReconcileCompanyAdministratorPermissions.cs
    // and, for the OBT-IAM-09 narrowing (onboarding:view/manage, support:manage removed), with
    // src/Modules/HR.Modules.Identity/Migrations/*_OBT_IAM09_RemoveCompanyAdministratorOnboardingSupport.cs
    private const string ReconcileSql = """
        DELETE FROM identity.role_permissions
        WHERE role_id = '00000000-0000-0000-0000-000000000006'
          AND permission_id NOT IN (
            '00000000-0000-0000-0001-000000000011',
            '00000000-0000-0000-0001-000000000012',
            '00000000-0000-0000-0001-000000000021'
          );
        """;

    private static readonly Guid[] AllowList =
    {
        SystemPermissions.CompanyRead,
        SystemPermissions.CompanyEdit,
        SystemPermissions.SubscriptionManage,
    };

    private IdentityAuthorizationService BuildService() => new(fixture.BuildContext(), Clock);

    // ---------------------------------------------------------------------
    // Reconciliation migration DELETE — idempotent + correct
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ReconcileSql_Removes_Obsolete_Grants_Keeps_AllowList_And_Is_Idempotent()
    {
        Guid[] obsolete =
        {
            SystemPermissions.EmployeeRead,
            SystemPermissions.LeaveApprove,
            SystemPermissions.SicknessManage,
            SystemPermissions.DocumentManage,
        };

        await using (var seed = fixture.BuildContext())
        {
            foreach (var permissionId in obsolete)
            {
                var exists = await seed.RolePermissions.AnyAsync(rp =>
                    rp.RoleId == SystemRoles.CompanyAdministrator && rp.PermissionId == permissionId);
                if (!exists)
                    seed.RolePermissions.Add(RolePermission.Create(SystemRoles.CompanyAdministrator, permissionId));
            }

            await seed.SaveChangesAsync();
        }

        await using var db = fixture.BuildContext();

        var firstRun = await db.Database.ExecuteSqlRawAsync(ReconcileSql);
        Assert.Equal(obsolete.Length, firstRun);

        var secondRun = await db.Database.ExecuteSqlRawAsync(ReconcileSql);
        Assert.Equal(0, secondRun);

        var remaining = await db.RolePermissions
            .Where(rp => rp.RoleId == SystemRoles.CompanyAdministrator)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        Assert.Equal(AllowList.ToHashSet(), remaining.ToHashSet());
        foreach (var permissionId in obsolete)
            Assert.DoesNotContain(permissionId, remaining);
    }

    // ---------------------------------------------------------------------
    // Effective-access override semantics (api/me is computed from these)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Expired_Grant_Override_Grants_Nothing()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(userId, $"iam08-{userId:N}@test.com", "hash", "Test", "User", Now));
            db.Roles.Add(Role.Create(roleId, $"Iam08ExpiredGrant{userId:N}", Now));
            db.EmployeeRoleOverrides.Add(EmployeeRoleOverride.Create(
                Guid.NewGuid(), userId, roleId, EmployeeRoleOverrideType.Grant, "iam-08", Now.AddSeconds(-1), Now));
            await db.SaveChangesAsync();
        }

        var svc = BuildService();

        Assert.DoesNotContain(roleId, await svc.GetEffectiveRolesAsync(userId));
        Assert.Empty(await svc.GetEffectivePermissionsAsync(userId));
    }

    // ---------------------------------------------------------------------
    // Removing HrAdministrator drops the inherited employee permissions/role
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Removing_HrAdministrator_UserRole_Drops_Employee_Permissions_And_Role()
    {
        var userId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Users.Add(ApplicationUser.Create(userId, $"iam08-hr-{userId:N}@test.com", "hash", "Test", "User", Now));
            db.UserRoles.Add(UserRole.Create(userId, SystemRoles.Employee, Now));
            db.UserRoles.Add(UserRole.Create(userId, SystemRoles.CompanyAdministrator, Now));
            db.UserRoles.Add(UserRole.Create(userId, SystemRoles.HrAdministrator, Now));
            await db.SaveChangesAsync();
        }

        var before = await BuildService().GetEffectivePermissionsAsync(userId);
        Assert.Contains(SystemPermissions.EmployeeRead, before);
        Assert.Contains(SystemRoles.HrAdministrator, await BuildService().GetEffectiveRolesAsync(userId));

        await using (var db = fixture.BuildContext())
        {
            var hrRole = await db.UserRoles.SingleAsync(ur =>
                ur.UserId == userId && ur.RoleId == SystemRoles.HrAdministrator);
            db.UserRoles.Remove(hrRole);
            await db.SaveChangesAsync();
        }

        var after = await BuildService().GetEffectivePermissionsAsync(userId);
        Assert.DoesNotContain(SystemPermissions.EmployeeRead, after);
        Assert.DoesNotContain(SystemPermissions.EmployeeEdit, after);
        Assert.DoesNotContain(SystemPermissions.EmployeeCreate, after);
        Assert.DoesNotContain(SystemPermissions.EmployeeDelete, after);
        Assert.DoesNotContain(SystemRoles.HrAdministrator, await BuildService().GetEffectiveRolesAsync(userId));
    }
}
