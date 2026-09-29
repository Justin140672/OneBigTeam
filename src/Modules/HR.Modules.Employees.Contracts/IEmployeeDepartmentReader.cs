namespace HR.Modules.Employees.Contracts;

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
