using HR.Modules.Employees.Contracts;

namespace HR.Modules.Identity.Tests.Infrastructure;

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
