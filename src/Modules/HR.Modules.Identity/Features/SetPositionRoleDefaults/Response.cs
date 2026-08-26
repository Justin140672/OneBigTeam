namespace HR.Modules.Identity.Features.SetPositionRoleDefaults;

internal sealed record SetPositionRoleDefaultsResponse(Guid PositionProfileId, IReadOnlyList<Guid> RoleIds);
