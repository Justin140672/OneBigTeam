namespace HR.Modules.Employees.Contracts;

public interface IEmployeeImportLookupReader
{
    Task<bool> EmployeeNumberExistsAsync(Guid companyId, string employeeNumber, CancellationToken cancellationToken);

    Task<bool> WorkEmailExistsAsync(Guid companyId, string workEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a human-readable reference (an employee number, a work email, OR a full name
    /// "First Last", case-insensitive) to a real employee's id, for manager-reference validation.
    /// Null if no match.
    /// </summary>
    Task<Guid?> FindEmployeeIdByReferenceAsync(Guid companyId, string reference, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the id of the company's initial (seed) admin employee — the single employee record
    /// auto-created at self-service signup, see Employee.IsInitialCompanyAdmin — if its Work Email
    /// matches <paramref name="workEmail"/>. Null otherwise (including when the matching employee
    /// exists but is not the seed admin). Used only by employee import: a matching row is treated
    /// as an update to this seed record rather than a duplicate-email error.
    /// </summary>
    Task<Guid?> TryFindInitialCompanyAdminEmployeeIdByWorkEmailAsync(Guid companyId, string workEmail, CancellationToken cancellationToken);
}
