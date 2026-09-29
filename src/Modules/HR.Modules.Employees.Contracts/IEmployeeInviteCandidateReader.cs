namespace HR.Modules.Employees.Contracts;

public sealed record EmployeeInviteCandidate(
    Guid EmployeeId,
    string FullName,
    string? WorkEmail,
    Guid? PositionProfileId,
    string? PositionTitle);

public interface IEmployeeInviteCandidateReader
{
    Task<IReadOnlyList<EmployeeInviteCandidate>> GetCandidatesAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}
