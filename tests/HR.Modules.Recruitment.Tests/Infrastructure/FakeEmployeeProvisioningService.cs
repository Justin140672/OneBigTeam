using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeEmployeeProvisioningService : IEmployeeProvisioningService
{
    private readonly Result<Guid> _result;

    public FakeEmployeeProvisioningService(Result<Guid>? result = null)
    {
        _result = result ?? Result.Success(Guid.NewGuid());
    }

    public List<EmployeeProvisioningRequest> Requests { get; } = [];

    public Task<Result<Guid>> CreateFromCandidateAsync(
        EmployeeProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_result);
    }
}
