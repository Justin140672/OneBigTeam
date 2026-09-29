namespace HR.Modules.Employees.Contracts;

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
