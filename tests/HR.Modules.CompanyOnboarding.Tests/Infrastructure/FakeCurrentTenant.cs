using HR.SharedKernel;

namespace HR.Modules.CompanyOnboarding.Tests.Infrastructure;

internal sealed class FakeCurrentTenant : ICurrentTenant
{
    public FakeCurrentTenant(string? tenantId)
    {
        TenantId = tenantId;
    }

    public string? TenantId { get; }

    public bool HasTenant => TenantId is not null;

    public static FakeCurrentTenant For(Guid tenantId) => new(tenantId.ToString());

    public static FakeCurrentTenant For(string tenantId) => new(tenantId);

    public static FakeCurrentTenant None => new(null);
}
