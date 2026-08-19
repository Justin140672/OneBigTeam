namespace HR.Modules.Employees.Contracts;

public sealed record EmployeeImportCreateRequest(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string? PreferredName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    Guid DepartmentId,
    Guid LocationId,
    Guid EmploymentTypeId,
    Guid PositionProfileId,
    // Null/blank when the company is in Automatic employee-numbering mode: the writer generates
    // the number itself via IEmployeeNumberGenerator in that case. Always populated in Manual mode
    // (guaranteed by EmployeeStagingRowValidator's mode-aware requiredness check at staging time).
    string? EmployeeNumber,
    Guid ImportSessionId,
    Guid? ActorUserId);

public sealed record EmployeeImportCreateResult(
    Guid EmployeeId,
    string EmployeeNumber,
    DateOnly StartDate,
    Guid? ManagerId,
    Guid? PositionProfileId,
    DateOnly ProbationEndDate,
    Guid? DefaultLeavePolicyId);

public sealed record EmployeeImportWorkingPattern(WorkingDays? WorkingDays, decimal? HoursPerDay);

public sealed record EmployeeImportCompensation(
    decimal SalaryAmount,
    string SalaryType,
    string Currency,
    decimal? HoursPerWeek,
    decimal? Fte);

/// <summary>
/// Cross-module write surface used exclusively by the DataImport confirm step to create
/// employees and their related records without DataImport referencing HR.Modules.Employees
/// directly. Implemented in HR.Modules.Employees.Services and DI-registered in EmployeesModule.
/// </summary>
public interface IEmployeeImportWriter
{
    Task<EmployeeImportCreateResult> CreateEmployeeAsync(
        EmployeeImportCreateRequest request, CancellationToken cancellationToken);

    Task SetWorkingPatternAsync(
        Guid companyId, Guid employeeId, EmployeeImportWorkingPattern pattern, CancellationToken cancellationToken);

    Task CreateOpeningCompensationAsync(
        Guid companyId, Guid employeeId, DateOnly effectiveFrom, EmployeeImportCompensation compensation, CancellationToken cancellationToken);

    /// <summary>
    /// Assigns a manager to an already-created employee, replicating AssignManager's circular
    /// hierarchy guard. Returns false (without making any change) if the assignment would
    /// create a cycle or the manager does not exist/is terminated.
    /// </summary>
    Task<bool> TryAssignManagerAsync(
        Guid companyId, Guid employeeId, Guid managerId, CancellationToken cancellationToken);
}
