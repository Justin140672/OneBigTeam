using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

public interface IEmployeeProvisioningService
{
    Task<Result<Guid>> CreateFromCandidateAsync(
        EmployeeProvisioningRequest request,
        CancellationToken cancellationToken);

    Task MarkAsInitialCompanyAdminAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}
