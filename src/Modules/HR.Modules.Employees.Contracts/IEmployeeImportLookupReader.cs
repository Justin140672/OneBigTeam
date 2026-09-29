namespace HR.Modules.Employees.Contracts;

public interface IEmployeeImportLookupReader
{
    Task<bool> EmployeeNumberExistsAsync(Guid companyId, string employeeNumber, CancellationToken cancellationToken);

    Task<bool> WorkEmailExistsAsync(Guid companyId, string workEmail, CancellationToken cancellationToken);

    Task<Guid?> FindEmployeeIdByReferenceAsync(Guid companyId, string reference, CancellationToken cancellationToken);

    Task<Guid?> TryFindInitialCompanyAdminEmployeeIdByWorkEmailAsync(Guid companyId, string workEmail, CancellationToken cancellationToken);
}
