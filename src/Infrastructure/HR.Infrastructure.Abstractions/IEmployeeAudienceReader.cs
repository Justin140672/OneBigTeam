namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Cross-module read access to an employee's department/location, and to counting/listing which
/// active employees fall within a given department/location scope — used by the Documents module
/// to resolve and enforce a shared document's audience without depending on the Employees
/// module's own persistence directly.
/// </summary>
public interface IEmployeeAudienceReader
{
    /// <summary>Returns null for both if the employee isn't found.</summary>
    Task<(Guid? DepartmentId, Guid? LocationId)> GetEmployeeAudienceAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken);

    Task<bool> DepartmentExistsAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken);

    Task<bool> LocationExistsAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken);

    Task<string?> GetDepartmentNameAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken);

    Task<string?> GetLocationNameAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken);

    /// <summary>
    /// Active employee IDs within the given scope: both null means every active employee in the
    /// company, departmentId scopes to that department, locationId scopes to that location.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
        Guid companyId, Guid? departmentId, Guid? locationId, CancellationToken cancellationToken);
}
