namespace HR.Modules.Employees.Contracts;

public interface IManagerReader
{
    Task<Guid?> GetManagerIdAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}
