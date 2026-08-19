namespace HR.Modules.Employees.Contracts;

// Invitation/account status taxonomy shared by Identity's User Administration list
// (ListUsers/GetUserDetails) and the Employees module's "User Account" column (EmployeeList).
//
// Derivation rules (documented here as the single source of truth so both call sites agree):
//   NoUser              — no ApplicationUser and no non-cancelled UserInvite exists for the employee.
//   PendingInvitation   — a UserInvite exists, is not claimed, not cancelled, and not expired.
//   InvitationExpired   — a UserInvite exists, is not claimed, not cancelled, but its ExpiresAt has passed.
//   Active              — an ApplicationUser exists and IsActive is true.
//   Disabled            — an ApplicationUser exists and IsActive is false.
public enum EmployeeUserAccountStatus
{
    NoUser = 0,
    PendingInvitation = 1,
    InvitationExpired = 2,
    Active = 3,
    Disabled = 4,
}

public sealed record EmployeeUserAccountSummary(
    Guid EmployeeId,
    EmployeeUserAccountStatus Status,
    DateTimeOffset? LastLoginAt);
