namespace HR.Modules.Identity.Features.UpdateUserRoles;

internal sealed record UpdateUserRolesResponse(Guid UserId, IReadOnlyList<Guid> RoleIds);
