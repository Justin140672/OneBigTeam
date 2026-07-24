namespace HR.Infrastructure.Abstractions;

/// <summary>An employee's audience-relevant attributes — the fields a document audience rule can match against.</summary>
public sealed record EmployeeAudienceProfile(Guid? DepartmentId, Guid? LocationId, Guid? PositionProfileId);

/// <summary>An employee's department/location, resolved to both id and display name in one batch.</summary>
public sealed record EmployeeAudienceDetail(
    Guid EmployeeId, Guid? DepartmentId, string? DepartmentName, Guid? LocationId, string? LocationName,
    Guid? ManagerId, string? ManagerName);

/// <summary>
/// Cross-module read access to an employee's department/location/position, and to counting/
/// listing which active employees fall within a given audience scope — used by the Documents
/// module to resolve and enforce a shared document's audience without depending on the
/// Employees module's own persistence directly.
/// </summary>
public interface IEmployeeAudienceReader
{
    /// <summary>Returns null if the employee isn't found.</summary>
    Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken);

    /// <summary>Batch department/location lookup for a set of employees — avoids N+1 queries when rendering an employee list.</summary>
    Task<IReadOnlyList<EmployeeAudienceDetail>> GetEmployeeAudienceDetailsAsync(
        Guid companyId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken);

    Task<bool> DepartmentExistsAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken);

    Task<bool> LocationExistsAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken);

    Task<bool> PositionProfileExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    Task<bool> EmployeeExistsAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);

    Task<string?> GetDepartmentNameAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken);

    Task<string?> GetLocationNameAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken);

    Task<string?> GetPositionProfileNameAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    /// <summary>
    /// Active employee IDs matching at least one of the given audience scopes (a department they
    /// belong to, a location they belong to, a position they hold, or their own id being listed).
    /// All four collections empty means every active employee in the company (the "all employees"
    /// rule).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> departmentIds,
        IReadOnlyCollection<Guid> locationIds,
        IReadOnlyCollection<Guid> positionProfileIds,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken);
}
