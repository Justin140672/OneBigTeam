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
    string? EmployeeNumber,
    Guid ImportSessionId,
    Guid? ActorUserId,
    string? Address = null,
    DateOnly? ProbationEndDate = null);

public sealed record EmployeeImportCreateResult(
    Guid EmployeeId,
    string EmployeeNumber,
    DateOnly StartDate,
    Guid? ManagerId,
    Guid? PositionProfileId,
    DateOnly ProbationEndDate,
    Guid? DefaultLeavePolicyId);

public sealed record EmployeeImportWorkingPattern(WorkingDays? WorkingDays, decimal? HoursPerDay);

// Hours Per Week and FTE are deliberately NOT part of this record: they are always calculated from
// the employee's working pattern (working days x hours per day, see
// WorkingPatternCompensationCalculator) rather than imported directly — see backlog items on
// removing the Hours Per Week / FTE import columns.
public sealed record EmployeeImportCompensation(
    decimal SalaryAmount,
    string SalaryType,
    string Currency);

public interface IEmployeeImportWriter
{
    Task<EmployeeImportCreateResult> CreateEmployeeAsync(
        EmployeeImportCreateRequest request, CancellationToken cancellationToken);

    Task<EmployeeImportCreateResult> UpdateEmployeeAsync(
        Guid existingEmployeeId, EmployeeImportCreateRequest request, CancellationToken cancellationToken);

    Task SetWorkingPatternAsync(
        Guid companyId, Guid employeeId, EmployeeImportWorkingPattern pattern, CancellationToken cancellationToken);

    Task CreateOpeningCompensationAsync(
        Guid companyId, Guid employeeId, DateOnly effectiveFrom, EmployeeImportCompensation compensation, CancellationToken cancellationToken);

    Task<bool> TryAssignManagerAsync(
        Guid companyId, Guid employeeId, Guid managerId, CancellationToken cancellationToken);

    Task<EmployeeImportCreateResult?> GetImportSnapshotAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}
