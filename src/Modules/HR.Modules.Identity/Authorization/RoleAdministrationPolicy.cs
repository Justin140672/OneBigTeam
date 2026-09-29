using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Authorization;

/// <summary>
/// IAM-02: defines which system roles a given administrator role is allowed to grant or revoke on
/// another user (or on themselves) via Features/UpdateUserRoles.
///
/// Company Administrator and HR Administrator are deliberately kept as mirror-image, mutually
/// exclusive domains everywhere else in this module (see the "employee:manage"/"company:manage"/
/// "hr-settings:manage" policy comments in IdentityModule.AddRolePolicies) — a Company Administrator
/// is scoped to company profile/settings and must not reach into HR/employee territory, and an HR
/// Administrator must not reach into company-level configuration. Role administration follows the
/// same split: neither admin role can grant or revoke the other's role, which is what stops a
/// Company Administrator from self-elevating into HR Administrator (or vice versa) purely through
/// role assignment.
///
/// This is intentionally a fixed, explicit matrix rather than a permission-count/superset
/// comparison — SystemRoles is itself a small, fixed set (Employee/Manager/Recruiter/
/// HrAdministrator/CompanyAdministrator), and an explicit allow-list is easier to reason about and
/// audit for a security-sensitive capability than a derived calculation. If the platform later
/// grows a true custom-role system this will need to be reconciled — flagged for IAM-06.
/// </summary>
internal static class RoleAdministrationPolicy
{
    private static readonly IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> AdministrableRolesByHeldRole =
        new Dictionary<Guid, IReadOnlySet<Guid>>
        {
            [SystemRoles.CompanyAdministrator] = new HashSet<Guid>
            {
                SystemRoles.Employee,
                SystemRoles.Manager,
                SystemRoles.Recruiter,
                SystemRoles.CompanyAdministrator,
            },

            [SystemRoles.HrAdministrator] = new HashSet<Guid>
            {
                SystemRoles.Employee,
                SystemRoles.Manager,
                SystemRoles.Recruiter,
                SystemRoles.HrAdministrator,
            },
        };

    public static IReadOnlySet<Guid> GetAdministrableRoles(IReadOnlySet<Guid> actorEffectiveRoles)
    {
        var administrable = new HashSet<Guid>();
        foreach (var roleId in actorEffectiveRoles)
        {
            if (AdministrableRolesByHeldRole.TryGetValue(roleId, out var roles))
            {
                administrable.UnionWith(roles);
            }
        }

        return administrable;
    }

    public static bool IsMandatory(Guid roleId) => roleId == SystemRoles.Employee;

    public static bool IsLockoutProtected(Guid roleId) =>
        roleId == SystemRoles.CompanyAdministrator || roleId == SystemRoles.HrAdministrator;
}
