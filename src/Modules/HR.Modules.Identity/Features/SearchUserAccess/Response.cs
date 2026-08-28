namespace HR.Modules.Identity.Features.SearchUserAccess;

internal sealed record RoleRef(Guid RoleId, string RoleName);

internal sealed record InheritedRoleRef(Guid RoleId, string RoleName, Guid PositionId, string PositionName);

internal sealed record OverrideRef(
    Guid OverrideId, Guid RoleId, string RoleName, string OverrideType, DateTimeOffset? ExpiresAt, bool IsExpiringSoon);

internal sealed record UserAccessSearchItem(
    Guid EmployeeId,
    Guid? UserId,
    string Name,
    string Email,
    IReadOnlyList<RoleRef> DirectRoles,
    IReadOnlyList<InheritedRoleRef> InheritedRoles,
    IReadOnlyList<OverrideRef> Overrides);

internal sealed record SearchUserAccessResponse(
    IReadOnlyList<UserAccessSearchItem> Items, int TotalCount, int Page, int PageSize);
