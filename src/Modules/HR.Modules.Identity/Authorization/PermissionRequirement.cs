using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Identity.Authorization;

/// <summary>
/// IAM-06: authorization requirement satisfied when the current user's effective permission set
/// (roles + position-inherited roles + employee-level overrides, resolved by
/// IAuthorizationService.HasPermissionAsync) contains <see cref="PermissionId"/>. Used by every
/// named capability policy registered in IdentityModule.AddRolePolicies so that policy evaluation
/// always goes through the single authoritative permission catalogue (Domain/SystemPermissions.cs
/// + Persistence/Configurations/RolePermissionConfiguration.cs) rather than a role list duplicated
/// inline per policy.
/// </summary>
internal sealed class PermissionRequirement(Guid permissionId) : IAuthorizationRequirement
{
    public Guid PermissionId { get; } = permissionId;
}
