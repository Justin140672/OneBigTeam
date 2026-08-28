using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// IAM-06: the core regression guard for the permission-based authorization mechanism. Enumerates
/// every (role, named policy) pair — every <see cref="SystemRoles"/> value crossed with every entry
/// in <see cref="PolicyCatalog.PermissionPolicies"/> — and asserts against the real migration-seeded
/// IdentityDbContext RolePermissions data (ground truth:
/// Persistence/Configurations/RolePermissionConfiguration.cs) whether that role satisfies that
/// policy. This proves the permission-based mechanism (PolicyCatalog + PermissionAuthorizationHandler)
/// reproduces exactly the same effective-authorization outcomes the old inline per-policy role lists
/// had, and that a future change to either RolePermissionConfiguration or PolicyCatalog can never
/// silently drift the API from what the UI's GetEffectiveAccess view reports (both read from the
/// same RolePermission data).
/// </summary>
// Ground-truth cross-reference: HR.Modules.Identity.Persistence.IdentityDbContext,
// HR.Modules.Identity.Persistence.Configurations.RolePermissionConfiguration.
[Collection("IdentityDatabase")]
public class PolicyMatrixTests(IdentityDatabaseFixture fixture)
{
    // Ground truth access matrix: policy name -> set of roles that hold the backing permission.
    // Derived from RolePermissionConfiguration.cs. Any role not listed for a given policy is
    // expected to fail that policy.
    private static readonly Dictionary<string, HashSet<Guid>> ExpectedGrantees = new()
    {
        ["employee:manage"] = [SystemRoles.HrAdministrator],
        ["company:manage"] = [SystemRoles.CompanyAdministrator],
        ["support:manage"] = [SystemRoles.HrAdministrator, SystemRoles.CompanyAdministrator],
        ["hr-settings:manage"] = [SystemRoles.HrAdministrator],
        ["users:view"] = [SystemRoles.HrAdministrator],
        ["users:manage"] = [SystemRoles.HrAdministrator],
        ["onboarding:view"] = [SystemRoles.HrAdministrator, SystemRoles.CompanyAdministrator],
        ["onboarding:manage"] = [SystemRoles.HrAdministrator, SystemRoles.CompanyAdministrator],
        ["subscription:manage"] = [SystemRoles.HrAdministrator, SystemRoles.CompanyAdministrator],
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
        ["candidate:view"] = [SystemRoles.Recruiter],
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
    public void Expected_Grant_Matrix_Covers_Every_Cataloged_Policy()
    {
        Assert.Equal(
            PolicyCatalog.PermissionPolicies.Keys.OrderBy(k => k, StringComparer.Ordinal),
            ExpectedGrantees.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }
}
