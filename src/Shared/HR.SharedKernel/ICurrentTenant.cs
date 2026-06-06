namespace HR.SharedKernel;

public interface ICurrentTenant
{
    string? TenantId { get; }

    bool HasTenant { get; }
}
