namespace HR.Web.Models;

// ── System roles ────────────────────────────────────────────────────────────
// Mirrors HR.Modules.Identity.Domain.SystemRoles (internal to that module, so the fixed
// GUID/name pairs are duplicated here for the role picker's DataSource). If a role is ever
// renamed or a new one added there, this list needs to be updated to match.
public record RoleOption(Guid Id, string Name);

public static class SystemRoleOptions
{
    // Every user always has this role — it's never optional, so callers that let an admin
    // pick roles (InviteUserDialog, ManageUserRolesDialog) pin it into the selection and
    // block its removal rather than letting it be unchecked like the others.
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

// ── GET /api/companies/{companyId}/users ────────────────────────────────────
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
    DateTimeOffset CreatedAt);

// ── GET /api/companies/{companyId}/users/{employeeId} ───────────────────────
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
    DateTimeOffset CreatedAt);

// ── GET /api/companies/{companyId}/users/{employeeId}/audit-history ────────
public record GetUserAuditHistoryResponse(List<UserAuditHistoryItemModel> Items);

public record UserAuditHistoryItemModel(
    DateTimeOffset OccurredAt,
    string EventType,
    string Summary,
    string? PerformedBy);

// ── POST /api/companies/{companyId}/employees/{employeeId}/invite-user ─────
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

// ── PUT /api/companies/{companyId}/users/{userId}/roles ─────────────────────
public record UpdateUserRolesRequest(
    Guid CompanyId,
    Guid UserId,
    List<Guid> RoleIds);

// ── Generic action responses (resend/cancel invite, disable/enable user) ───
public record UserActionResponse(bool Success);

// ── Employee role overrides (IAM-04) ────────────────────────────────────────
// Mirrors HR.Modules.Identity.Domain.EmployeeRoleOverrideType (internal to that module), the
// same duplication rationale as SystemRoleOptions above — serialized as a string via the global
// JsonStringEnumConverter, so the member names below must match "Grant"/"Deny" exactly.
public enum EmployeeRoleOverrideType
{
    Grant,
    Deny,
}

// GET /api/companies/{companyId}/users/{userId}/role-overrides
public record ListEmployeeRoleOverridesResponse(List<EmployeeRoleOverrideModel> Overrides);

public record EmployeeRoleOverrideModel(
    Guid RoleId,
    EmployeeRoleOverrideType OverrideType,
    string Reason,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset AssignedAt,
    Guid? AssignedBy);

// POST /api/companies/{companyId}/users/{userId}/role-overrides
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

// DELETE /api/companies/{companyId}/users/{userId}/role-overrides/{roleId}
public record RemoveEmployeeRoleOverrideResponse(Guid UserId, Guid RoleId);
