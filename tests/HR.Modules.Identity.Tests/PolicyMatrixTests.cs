using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class PolicyMatrixTests(IdentityDatabaseFixture fixture)
{
    private static readonly Dictionary<string, HashSet<Guid>> ExpectedGrantees = new()
    {
        ["employee:manage"] = [SystemRoles.HrAdministrator],
        ["employee:read"] = [SystemRoles.Manager, SystemRoles.Recruiter, SystemRoles.HrAdministrator],
        ["company:manage"] = [SystemRoles.CompanyAdministrator],
        ["support:manage"] = [SystemRoles.HrAdministrator],
        ["support:request"] = [SystemRoles.HrAdministrator],
        ["hr-settings:manage"] = [SystemRoles.HrAdministrator],
        ["users:view"] = [SystemRoles.HrAdministrator],
        ["users:manage"] = [SystemRoles.HrAdministrator],
        ["onboarding:view"] = [SystemRoles.HrAdministrator],
        ["onboarding:manage"] = [SystemRoles.HrAdministrator],
        ["subscription:manage"] = [SystemRoles.CompanyAdministrator],
        ["leave:request"] = [SystemRoles.Employee, SystemRoles.Manager, SystemRoles.HrAdministrator],
        ["leave:approve"] = [SystemRoles.Manager, SystemRoles.HrAdministrator],
        ["leave:manage"] = [SystemRoles.HrAdministrator],
        ["probation:manage"] = [SystemRoles.HrAdministrator],
        ["probation:review"] = [SystemRoles.Manager, SystemRoles.HrAdministrator],
        ["sickness:review"] = [SystemRoles.Manager, SystemRoles.HrAdministrator],
        ["sickness:manage"] = [SystemRoles.HrAdministrator],
        ["sickness:view-team"] = [SystemRoles.Manager, SystemRoles.HrAdministrator],
        ["asset:view"] = [SystemRoles.Employee, SystemRoles.Manager, SystemRoles.HrAdministrator],
        ["recruitment:manage"] = [SystemRoles.Recruiter],
        ["recruitment:view"] = [SystemRoles.Employee, SystemRoles.Manager, SystemRoles.Recruiter, SystemRoles.HrAdministrator],
        ["shared-document:view-published"] = [SystemRoles.Employee, SystemRoles.Manager, SystemRoles.Recruiter, SystemRoles.HrAdministrator],
        ["shared-document:manage"] = [SystemRoles.HrAdministrator],
        ["shared-document:publish"] = [SystemRoles.HrAdministrator],
        ["shared-document:archive"] = [SystemRoles.HrAdministrator],
        ["shared-document:view-acknowledgement-status"] = [SystemRoles.HrAdministrator],
        ["reporting:view"] = [SystemRoles.Manager, SystemRoles.Recruiter, SystemRoles.HrAdministrator],
        ["reporting:view-recruitment"] = [SystemRoles.Recruiter],
        ["reporting:view-hr"] = [SystemRoles.HrAdministrator],
        ["reporting:view-employee-starter"] = [SystemRoles.HrAdministrator, SystemRoles.Recruiter],
        ["reporting:view-leave-summary"] = [SystemRoles.HrAdministrator, SystemRoles.Manager],
        ["reporting:view-probation"] = [SystemRoles.HrAdministrator, SystemRoles.Manager],
        ["reporting:view-onboarding"] = [SystemRoles.HrAdministrator, SystemRoles.Manager],
        ["reporting:view-workload-actions"] = [SystemRoles.HrAdministrator, SystemRoles.Manager],
        // Ticket 6B: anonymous equality & diversity report — HR Administrator only.
        ["reporting:view-equality"] = [SystemRoles.HrAdministrator],
        ["compliance:view"] = [SystemRoles.HrAdministrator],
    };

    private static readonly IReadOnlyDictionary<Guid, string> RoleNames = new Dictionary<Guid, string>
    {
        [SystemRoles.Employee] = nameof(SystemRoles.Employee),
        [SystemRoles.Manager] = nameof(SystemRoles.Manager),
        [SystemRoles.Recruiter] = nameof(SystemRoles.Recruiter),
        [SystemRoles.HrAdministrator] = nameof(SystemRoles.HrAdministrator),
        [SystemRoles.CompanyAdministrator] = nameof(SystemRoles.CompanyAdministrator),
    };

    public static IEnumerable<object[]> RolePolicyPairs()
    {
        var roles = new[]
        {
            SystemRoles.Employee,
            SystemRoles.Manager,
            SystemRoles.Recruiter,
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator,
        };

        foreach (var policyName in PolicyCatalog.PermissionPolicies.Keys)
            foreach (var roleId in roles)
                yield return [policyName, roleId];
    }

    [Theory]
    [MemberData(nameof(RolePolicyPairs))]
    public async Task Role_Policy_Access_Matches_The_Expected_Grant_Matrix(string policyName, Guid roleId)
    {
        Assert.True(
            ExpectedGrantees.TryGetValue(policyName, out var grantees),
            $"No expected-grantees entry defined for policy '{policyName}' — update ExpectedGrantees.");

        var expectedAccess = grantees.Contains(roleId);
        var permissionId = PolicyCatalog.PermissionPolicies[policyName];

        await using var db = fixture.BuildContext();
        var actualAccess = await db.RolePermissions
            .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

        Assert.True(
            expectedAccess == actualAccess,
            $"Policy '{policyName}' access mismatch for role {RoleNames.GetValueOrDefault(roleId, roleId.ToString())}: " +
            $"expected {expectedAccess}, got {actualAccess}.");
    }

    [Fact]
    public async Task Employee_No_Longer_Receives_Support_Request_Permission()
    {
        await using var db = fixture.BuildContext();

        Assert.False(await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == SystemRoles.Employee && rp.PermissionId == SystemPermissions.SupportRequest));
        Assert.True(await db.Roles.AnyAsync(r => r.Id == SystemRoles.Employee));
    }

    [Fact]
    public async Task Migration_Removes_Employee_Support_Request_Grant_Without_Removing_Permission_Or_Hr_Grant()
    {
        const string previousMigration = "20260929162926_MoveAccountStateToUserProfiles";

        await using var db = fixture.BuildContext();
        var migrator = db.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(previousMigration);

            Assert.True(await db.RolePermissions.AnyAsync(rp =>
                rp.RoleId == SystemRoles.Employee && rp.PermissionId == SystemPermissions.SupportRequest));
        }
        finally
        {
            await migrator.MigrateAsync();
        }

        Assert.False(await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == SystemRoles.Employee && rp.PermissionId == SystemPermissions.SupportRequest));
        Assert.True(await db.Permissions.AnyAsync(p => p.Id == SystemPermissions.SupportRequest));
        Assert.True(await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == SystemRoles.HrAdministrator && rp.PermissionId == SystemPermissions.SupportRequest));
        Assert.True(await db.RolePermissions.AnyAsync(rp =>
            rp.RoleId == SystemRoles.HrAdministrator && rp.PermissionId == SystemPermissions.SupportManage));
    }

    [Fact]
    public void Expected_Grant_Matrix_Covers_Every_Cataloged_Policy()
    {
        Assert.Equal(
            PolicyCatalog.PermissionPolicies.Keys.OrderBy(k => k, StringComparer.Ordinal),
            ExpectedGrantees.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }
}
