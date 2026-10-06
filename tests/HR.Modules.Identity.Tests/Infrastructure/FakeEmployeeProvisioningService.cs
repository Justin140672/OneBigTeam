using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeEmployeeProvisioningService : IEmployeeProvisioningService
{
    public int CallCount { get; private set; }

    public List<EmployeeProvisioningRequest> Requests { get; } = [];

    public bool ShouldFail { get; set; }

    public Guid EmployeeIdToReturn { get; set; } = Guid.NewGuid();

    public List<(Guid CompanyId, Guid EmployeeId)> MarkedAsInitialCompanyAdmin { get; } = [];

    public bool ShouldThrowOnMark { get; set; }

    public Func<Task>? BeforeCreateEffect { get; set; }

    public Func<Task>? BeforeMarkEffect { get; set; }

    public async Task MarkAsInitialCompanyAdminAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        if (BeforeMarkEffect is { } before)
        {
            await before();
        }

        if (ShouldThrowOnMark)
        {
            throw new InvalidOperationException("Simulated mark-as-admin failure.");
        }

        MarkedAsInitialCompanyAdmin.Add((companyId, employeeId));
    }

    public async Task<Result<Guid>> CreateFromCandidateAsync(
        EmployeeProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        Requests.Add(request);

        if (BeforeCreateEffect is { } before)
        {
            await before();
        }

        return ShouldFail
            ? Result.Failure<Guid>(Error.Validation("Simulated employee provisioning failure."))
            : Result.Success(EmployeeIdToReturn);
    }
}
