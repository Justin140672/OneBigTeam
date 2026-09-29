namespace HR.Infrastructure.Abstractions;

public interface ILeavePolicyProvisioner
{
    Task<Guid> EnsureDefaultLeavePolicyAsync(Guid companyId, CancellationToken cancellationToken);
}
