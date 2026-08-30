using HR.Modules.Employees.Contracts;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IEmployeeInviteCandidateReader"/> (ADM-01). Returns whatever
/// candidate list it was constructed with, regardless of company id — the Employees-side
/// "non-former employees only" filtering is covered by EmployeeInviteCandidateReaderTests in
/// HR.Modules.Employees.Tests; here we only exercise Identity's own account/invite exclusion.
/// </summary>
internal sealed class FakeEmployeeInviteCandidateReader(params EmployeeInviteCandidate[] candidates)
    : IEmployeeInviteCandidateReader
{
    public Guid? LastCompanyId { get; private set; }

    public Task<IReadOnlyList<EmployeeInviteCandidate>> GetCandidatesAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        return Task.FromResult<IReadOnlyList<EmployeeInviteCandidate>>(candidates.ToList());
    }
}
