using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Identity.Authorization;

internal sealed class RoleRequirement(IReadOnlySet<Guid> allowedRoleIds) : IAuthorizationRequirement
{
    public IReadOnlySet<Guid> AllowedRoleIds { get; } = allowedRoleIds;
}
