namespace HR.SharedKernel.Contracts;

public interface IManagerReader
{
    /// <summary>
    /// Returns the manager's employee ID for the given employee, or null if they have no manager.
    /// </summary>
    Task<Guid?> GetManagerIdAsync(Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}
