using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class Iam09ReconciliationMigrationTests(IdentityDatabaseFixture fixture)
{
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
