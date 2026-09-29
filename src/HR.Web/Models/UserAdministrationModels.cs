namespace HR.Web.Models;

public record RoleOption(Guid Id, string Name);

public static class SystemRoleOptions
{
    public static readonly Guid EmployeeRoleId = new("00000000-0000-0000-0000-000000000001");

    public static readonly IReadOnlyList<RoleOption> All =
    [
        new(EmployeeRoleId, "Employee"),
        new(new Guid("00000000-0000-0000-0000-000000000002"), "Manager"),
        new(new Guid("00000000-0000-0000-0000-000000000003"), "Recruiter"),
        new(new Guid("00000000-0000-0000-0000-000000000004"), "HR Administrator"),
        new(new Guid("00000000-0000-0000-0000-000000000006"), "Company Administrator"),
    ];

    public static string NameFor(Guid roleId) =>
        All.FirstOrDefault(r => r.Id == roleId)?.Name ?? "Unknown Role";
}

public record ListUsersResponse(
    List<UserListItemModel> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record UserListItemModel(
    Guid EmployeeId,
    Guid? UserId,
    string Name,
    string Email,
    List<Guid> RoleIds,
    List<string> RoleNames,
    string AccountStatus,
    string? InvitationStatus,
    Guid? InviteId,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    Guid? PositionProfileId = null,
    string? PositionTitle = null);

public record GetUserDetailResponse(
    Guid EmployeeId,
    Guid? UserId,
    string Name,
    string Email,
    List<Guid> RoleIds,
    List<string> RoleNames,
    string AccountStatus,
    string? InvitationStatus,
    Guid? InviteId,
    DateTimeOffset? InviteExpiresAt,
    string? CreatedByName,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    Guid? PositionProfileId = null,
    string? PositionTitle = null);

public record GetInvitableEmployeesResponse(List<InvitableEmployeeModel> Items);

public record InvitableEmployeeModel(
    Guid EmployeeId,
    string Name,
    string? WorkEmail,
    Guid? PositionProfileId,
    string? PositionTitle);

public record GetUserAuditHistoryResponse(List<UserAuditHistoryItemModel> Items);

public record UserAuditHistoryItemModel(
    DateTimeOffset OccurredAt,
    string EventType,
    string Summary,
    string? PerformedBy);

public record InviteEmployeeUserRequest(
    Guid CompanyId,
    Guid EmployeeId,
    string Email,
    List<Guid> RoleIds);

public record InviteEmployeeUserResponse(
    Guid InviteId,
    Guid EmployeeId,
    string Email,
    DateTimeOffset ExpiresAt);

public record UpdateUserRolesRequest(
    Guid CompanyId,
    Guid UserId,
    List<Guid> RoleIds);

public record UserActionResponse(bool Success);

public enum EmployeeRoleOverrideType
{
    Grant,
    Deny,
}

public record ListEmployeeRoleOverridesResponse(List<EmployeeRoleOverrideModel> Overrides);

public record EmployeeRoleOverrideModel(
    Guid RoleId,
    EmployeeRoleOverrideType OverrideType,
    string Reason,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset AssignedAt,
    Guid? AssignedBy);

public record AddEmployeeRoleOverrideRequest(
    Guid CompanyId,
    Guid UserId,
    Guid RoleId,
    EmployeeRoleOverrideType OverrideType,
    string Reason,
    DateTimeOffset? ExpiresAt);

public record AddEmployeeRoleOverrideResponse(
    Guid UserId,
    Guid RoleId,
    EmployeeRoleOverrideType OverrideType,
    string Reason,
    DateTimeOffset? ExpiresAt);

public record RemoveEmployeeRoleOverrideResponse(Guid UserId, Guid RoleId);

public record GetEffectiveAccessResponse(
    Guid EmployeeId,
    Guid? UserId,
    string EmployeeName,
    PositionSummaryModel? Position,
    List<RoleSummaryModel> DirectRoles,
    List<InheritedRoleModel> InheritedRoles,
    List<RoleOverrideModel> Overrides,
    List<EffectiveRoleModel> EffectiveRoles,
    List<EffectivePermissionModel> EffectivePermissions,
    List<DeniedPermissionModel> DeniedPermissions);

public record PositionSummaryModel(Guid Id, string Name);
public record RoleSummaryModel(Guid Id, string Name);
public record InheritedRoleModel(Guid RoleId, string RoleName, Guid PositionId, string PositionName);
public record RoleOverrideModel(Guid Id, Guid RoleId, string RoleName, string OverrideType, string Reason, DateTimeOffset? ExpiresAt, bool IsActive);
public record PermissionSourceModel(Guid RoleId, string RoleName, string Origin);
public record EffectiveRoleModel(Guid RoleId, string RoleName, List<string> Sources);
public record EffectivePermissionModel(Guid PermissionId, string PermissionName, string Scope, List<PermissionSourceModel> Sources);
public record DeniedPermissionModel(Guid PermissionId, string PermissionName, string Scope, Guid DeniedByRoleId, string DeniedByRoleName, Guid OverrideId, string Reason);
