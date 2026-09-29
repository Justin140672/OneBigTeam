namespace HR.Modules.Employees.Contracts;

public interface ICurrentEmployeeReader
{
    Task<IReadOnlyList<Guid>> GetCurrentEmployeeIdsAsync(Guid companyId, CancellationToken cancellationToken);
}
