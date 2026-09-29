namespace HR.Modules.Employees.Contracts;

public sealed record EmployeeAudienceProfile(Guid? DepartmentId, Guid? LocationId, Guid? PositionProfileId);

public sealed record EmployeeAudienceDetail(
    Guid EmployeeId, Guid? DepartmentId, string? DepartmentName, Guid? LocationId, string? LocationName,
    Guid? ManagerId, string? ManagerName);

public interface IEmployeeAudienceReader
{
    Task<EmployeeAudienceProfile?> GetEmployeeAudienceAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, EmployeeAudienceProfile>> GetEmployeeAudienceProfilesAsync(
        Guid companyId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeeAudienceDetail>> GetEmployeeAudienceDetailsAsync(
        Guid companyId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken);

    Task<bool> DepartmentExistsAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken);

    Task<bool> LocationExistsAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken);

    Task<bool> PositionProfileExistsAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    Task<bool> EmployeeExistsAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);

    Task<string?> GetDepartmentNameAsync(Guid companyId, Guid departmentId, CancellationToken cancellationToken);

    Task<string?> GetLocationNameAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken);

    Task<string?> GetPositionProfileNameAsync(Guid companyId, Guid positionProfileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> departmentIds,
        IReadOnlyCollection<Guid> locationIds,
        IReadOnlyCollection<Guid> positionProfileIds,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every employee ID in the company regardless of employment status (Draft/Active/
    /// Suspended/Leaving) — unlike <see cref="GetEligibleEmployeeIdsAsync"/>, which is scoped to
    /// document-audience matching and deliberately excludes anyone not Active. Used where a
    /// consumer genuinely needs the full roster, e.g. User Administration listing every employee
    /// who could have or already has an application user account, including those still onboarding.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAllEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken);
}
