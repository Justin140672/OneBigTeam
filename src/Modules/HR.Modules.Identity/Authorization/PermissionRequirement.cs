using Microsoft.AspNetCore.Authorization;

namespace HR.Modules.Identity.Authorization;

internal sealed class PermissionRequirement(Guid permissionId) : IAuthorizationRequirement
{
    public Guid PermissionId { get; } = permissionId;
}
