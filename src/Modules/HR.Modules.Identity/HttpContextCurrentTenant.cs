using HR.SharedKernel;

namespace HR.Modules.Identity;

internal sealed class HttpContextCurrentTenant(ICurrentUser currentUser) : ICurrentTenant
{
    public string? TenantId => currentUser.TenantId;

    public bool HasTenant => currentUser.TenantId is not null;
}
