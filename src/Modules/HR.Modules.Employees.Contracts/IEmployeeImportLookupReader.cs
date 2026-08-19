namespace HR.Modules.Employees.Contracts;

public interface IEmployeeImportLookupReader
{
    Task<bool> EmployeeNumberExistsAsync(Guid companyId, string employeeNumber, CancellationToken cancellationToken);

    Task<bool> WorkEmailExistsAsync(Guid companyId, string workEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a human-readable reference (an employee number OR a work email) to a real employee's id,
    /// for manager-reference validation. Null if no match.
    /// </summary>
    Task<Guid?> FindEmployeeIdByReferenceAsync(Guid companyId, string reference, CancellationToken cancellationToken);
}
