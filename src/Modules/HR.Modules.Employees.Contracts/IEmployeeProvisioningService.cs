using HR.SharedKernel;

namespace HR.Modules.Employees.Contracts;

public interface IEmployeeProvisioningService
{
    Task<Result<Guid>> CreateFromCandidateAsync(
        EmployeeProvisioningRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the given employee as the company's initial (seed) admin employee — see
    /// Employee.IsInitialCompanyAdmin. Only ever called once, immediately after self-service
    /// signup creates the admin's own Employee record (see SignUpHandler).
    /// </summary>
    Task MarkAsInitialCompanyAdminAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}
