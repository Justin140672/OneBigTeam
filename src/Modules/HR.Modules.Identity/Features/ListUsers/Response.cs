namespace HR.Modules.Identity.Features.ListUsers;

// Invitation status taxonomy (documented once here — GetUserDetails reuses the same derivation):
//   Claimed   — invite has been claimed (ApplicationUser exists for the employee), OR an
//               ApplicationUser exists with no tracked UserInvite row at all (e.g. a dev-seeded
//               persona created directly rather than through the invite flow).
//   Pending   — invite exists, not claimed, not cancelled, not expired.
//   Expired   — invite exists, not claimed, not cancelled, ExpiresAt has passed.
//   Cancelled — invite exists and was explicitly cancelled (Features/CancelInvite).
//
// Account status is independent of invitation status: an employee can only reach Active/Disabled
// once an ApplicationUser row exists for them (via Claimed invite, or seeded directly).
//
// Employees with neither an invite nor an ApplicationUser are simply not included in this
// response — see IEmployeeUserAccountStatusReader for the Employees-module-facing "NoUser"
// projection used by the Employee List column, which does need to represent that case explicitly.
internal sealed record UserAdministrationListItem(
    Guid EmployeeId,
    Guid? UserId,
    string Name,
    string Email,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNames,
    string AccountStatus,
    string InvitationStatus,
    Guid? InviteId,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);

internal sealed record ListUsersResponse(
    IReadOnlyList<UserAdministrationListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
