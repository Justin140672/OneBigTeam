namespace HR.Modules.Employees.Contracts;

public interface IEmployeeNameReader
{
    /// <summary>
    /// Returns a name map for the supplied employee IDs within a company.
    /// IDs not found are simply absent from the returned dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
