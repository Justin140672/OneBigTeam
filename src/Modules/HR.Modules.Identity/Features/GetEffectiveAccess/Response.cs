namespace HR.Modules.Identity.Features.GetEffectiveAccess;

internal sealed record GetEffectiveAccessResponse(
    Guid EmployeeId,
    Guid? UserId,
    string EmployeeName,
    PositionSummaryDto? Position,
    IReadOnlyList<RoleSummaryDto> DirectRoles,
    IReadOnlyList<InheritedRoleDto> InheritedRoles,
    IReadOnlyList<RoleOverrideDto> Overrides,
    IReadOnlyList<EffectiveRoleDto> EffectiveRoles,
    IReadOnlyList<EffectivePermissionDto> EffectivePermissions,
    IReadOnlyList<DeniedPermissionDto> DeniedPermissions);

internal sealed record PositionSummaryDto(Guid Id, string Name);
internal sealed record RoleSummaryDto(Guid Id, string Name);
internal sealed record InheritedRoleDto(Guid RoleId, string RoleName, Guid PositionId, string PositionName);
internal sealed record RoleOverrideDto(Guid Id, Guid RoleId, string RoleName, string OverrideType, string Reason, DateTimeOffset? ExpiresAt, bool IsActive);
internal sealed record PermissionSourceDto(Guid RoleId, string RoleName, string Origin); // Origin one of: "Direct", "Position:<PositionName>", "Override"
internal sealed record EffectiveRoleDto(Guid RoleId, string RoleName, IReadOnlyList<string> Sources); // Sources: "Direct", "Position:<PositionName>", "Override"
internal sealed record EffectivePermissionDto(Guid PermissionId, string PermissionName, string Scope, IReadOnlyList<PermissionSourceDto> Sources);
internal sealed record DeniedPermissionDto(Guid PermissionId, string PermissionName, string Scope, Guid DeniedByRoleId, string DeniedByRoleName, Guid OverrideId, string Reason);
