namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Batch lookup of an employee's department/position for a given set of employee IDs, as owned
/// by HR.Modules.Employees. Used by modules that need to display/group/filter by department for a
/// set of employees they don't own (e.g. HR.Modules.Leave's reporting readers), without a direct
/// module-to-module reference or database join. Employees not found are absent from the returned
/// dictionary.
/// </summary>
public interface IEmployeeDepartmentReader
{
    Task<IReadOnlyDictionary<Guid, EmployeeDepartmentInfo>> GetDepartmentsAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}

public sealed record EmployeeDepartmentInfo(
    Guid EmployeeId,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName);
