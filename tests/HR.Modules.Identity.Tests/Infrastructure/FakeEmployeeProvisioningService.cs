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

    public Task<Result<Guid>> CreateFromCandidateAsync(
        EmployeeProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        Requests.Add(request);

        return Task.FromResult(ShouldFail
            ? Result.Failure<Guid>(Error.Validation("Simulated employee provisioning failure."))
            : Result.Success(EmployeeIdToReturn));
    }
}
