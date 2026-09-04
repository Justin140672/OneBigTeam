using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// OBT-IAM-09 — dedicated correctness/idempotency coverage for the reconciliation migration
/// itself (20260904185201_OBT_IAM09_RemoveCompanyAdministratorOnboardingSupport), simulating a
/// database provisioned before the ticket: a Company Administrator row still holding
/// onboarding:view, onboarding:manage and support:manage alongside its permanent allow-list
/// (company:read, company:edit, subscription:manage).
///
/// <see cref="Iam08CompanyAdministratorPermissionsTests"/>'s ReconcileSql test already covers the
/// idempotency of a WHERE-NOT-IN-shaped reconciliation using unrelated obsolete permissions
/// (employee:read, leave:approve, etc.) left over from the IAM-08 migration. This file exercises
/// the actual per-row DELETE shape used by the OBT-IAM-09 migration itself, against the exact
/// three permission ids it targets.
/// </summary>
[Collection("IdentityDatabase")]
public class Iam09ReconciliationMigrationTests(IdentityDatabaseFixture fixture)
{
    // Mirrors the three per-row DeleteData calls in
    // src/Modules/HR.Modules.Identity/Migrations/20260904185201_OBT_IAM09_RemoveCompanyAdministratorOnboardingSupport.cs
    private const string ReconcileSql = """
        DELETE FROM identity.role_permissions
        WHERE role_id = '00000000-0000-0000-0000-000000000006'
          AND permission_id IN (
            '00000000-0000-0000-0001-000000000019',
            '00000000-0000-0000-0001-000000000020',
            '00000000-0000-0000-0001-000000000042'
          );
        """;

    private static readonly Guid[] RemovedGrants =
    {
        SystemPermissions.OnboardingView,
        SystemPermissions.OnboardingManage,
        SystemPermissions.SupportManage,
    };

    private static readonly Guid[] RetainedAllowList =
    {
        SystemPermissions.CompanyRead,
        SystemPermissions.CompanyEdit,
        SystemPermissions.SubscriptionManage,
    };

    [Fact]
    public async Task Migration_Removes_Onboarding_And_Support_Grants_Keeps_Allowlist_And_Is_Idempotent()
    {
        // Simulate a pre-OBT-IAM-09 database: Company Administrator still holds the three
        // now-obsolete grants alongside its permanent allow-list.
        await using (var seed = fixture.BuildContext())
        {
            foreach (var permissionId in RemovedGrants.Concat(RetainedAllowList))
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
        Assert.Equal(RemovedGrants.Length, firstRun);

        var secondRun = await db.Database.ExecuteSqlRawAsync(ReconcileSql);
        Assert.Equal(0, secondRun);

        var remaining = await db.RolePermissions
            .Where(rp => rp.RoleId == SystemRoles.CompanyAdministrator)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

        foreach (var removed in RemovedGrants)
            Assert.DoesNotContain(removed, remaining);

        foreach (var retained in RetainedAllowList)
            Assert.Contains(retained, remaining);
    }

    [Fact]
    public async Task Migration_Is_A_NoOp_On_A_Database_That_Never_Had_The_Obsolete_Grants()
    {
        await using (var seed = fixture.BuildContext())
        {
            foreach (var permissionId in RetainedAllowList)
            {
                var exists = await seed.RolePermissions.AnyAsync(rp =>
                    rp.RoleId == SystemRoles.CompanyAdministrator && rp.PermissionId == permissionId);
                if (!exists)
                    seed.RolePermissions.Add(RolePermission.Create(SystemRoles.CompanyAdministrator, permissionId));
            }

            await seed.SaveChangesAsync();
        }

        await using var db = fixture.BuildContext();

        var rowsAffected = await db.Database.ExecuteSqlRawAsync(ReconcileSql);

        Assert.Equal(0, rowsAffected);
    }
}
