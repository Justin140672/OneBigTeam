namespace HR.Infrastructure.Abstractions;

public interface ILeavePolicyReader
{
    Task<bool> ExistsAsync(Guid companyId, Guid leavePolicyId, CancellationToken cancellationToken);

    Task<Guid?> GetDefaultLeavePolicyIdAsync(Guid companyId, CancellationToken cancellationToken);
}
