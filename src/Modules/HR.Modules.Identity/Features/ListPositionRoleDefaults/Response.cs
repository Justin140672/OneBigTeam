namespace HR.Modules.Identity.Features.ListPositionRoleDefaults;

internal sealed record PositionRoleDefaultItem(
    Guid PositionProfileId,
    string Title,
    bool IsActive,
    IReadOnlyList<Guid> RoleIds);

internal sealed record ListPositionRoleDefaultsResponse(IReadOnlyList<PositionRoleDefaultItem> Positions);
