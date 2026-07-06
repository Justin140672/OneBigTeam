using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

public interface IEmployeeProvisioningService
{
    Task<Result<Guid>> CreateFromCandidateAsync(
        EmployeeProvisioningRequest request,
        CancellationToken cancellationToken);
}
