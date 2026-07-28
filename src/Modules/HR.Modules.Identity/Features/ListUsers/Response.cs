namespace HR.Modules.Identity.Features.ListUsers;

// Invitation status taxonomy (documented once here — GetUserDetails reuses the same derivation):
//   Claimed   — invite has been claimed (ApplicationUser exists for the employee).
//   Pending   — invite exists, not claimed, not cancelled, not expired.
//   Expired   — invite exists, not claimed, not cancelled, ExpiresAt has passed.
//   Cancelled — invite exists and was explicitly cancelled (Features/CancelInvite).
//
// Account status is independent of invitation status: an employee can only reach Active/Disabled
// once their invite is Claimed (i.e. an ApplicationUser row exists).
internal sealed record UserAdministrationListItem(
    Guid EmployeeId,
    Guid? UserId,
    string Name,
    string Email,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNames,
    string AccountStatus,
    string InvitationStatus,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);

internal sealed record ListUsersResponse(
    IReadOnlyList<UserAdministrationListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
