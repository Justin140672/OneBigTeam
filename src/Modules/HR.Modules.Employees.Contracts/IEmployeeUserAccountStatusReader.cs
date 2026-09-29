namespace HR.Modules.Employees.Contracts;

public interface IEmployeeUserAccountStatusReader
{
    Task<IReadOnlyDictionary<Guid, EmployeeUserAccountSummary>> GetStatusesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
