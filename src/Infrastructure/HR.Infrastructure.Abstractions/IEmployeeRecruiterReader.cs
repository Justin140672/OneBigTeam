namespace HR.Infrastructure.Abstractions;

public interface IEmployeeRecruiterReader
{
    Task<IReadOnlyDictionary<Guid, string>> GetRecruiterNamesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
