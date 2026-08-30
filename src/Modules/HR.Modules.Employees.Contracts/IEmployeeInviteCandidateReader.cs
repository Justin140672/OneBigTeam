namespace HR.Modules.Employees.Contracts;

/// <summary>
/// A single employee who could be sent a user-account invitation, with the details the
/// User Administration "Invite user" workflow needs to present and pre-fill: display name,
/// work email (may be blank if none is on file) and current position.
/// </summary>
public sealed record EmployeeInviteCandidate(
    Guid EmployeeId,
    string FullName,
    string? WorkEmail,
    Guid? PositionProfileId,
    string? PositionTitle);

/// <summary>
/// Cross-module read port (implemented in HR.Modules.Employees, consumed by HR.Modules.Identity's
/// User Administration invite workflow) that lists employees eligible to be invited as users —
/// i.e. current (non-former) employees. Identity applies the final "already has an account or a
/// pending invitation" exclusion itself, since it owns ApplicationUser / UserProfile / UserInvite.
/// </summary>
public interface IEmployeeInviteCandidateReader
{
    Task<IReadOnlyList<EmployeeInviteCandidate>> GetCandidatesAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}
