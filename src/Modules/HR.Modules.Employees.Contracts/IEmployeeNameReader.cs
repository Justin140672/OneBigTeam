namespace HR.Modules.Employees.Contracts;

public interface IEmployeeNameReader
{
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
