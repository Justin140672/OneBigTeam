using HR.SharedKernel;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeCurrentUser(Guid? userId, string? email = null, string? tenantId = null, bool isAuthenticated = true) : ICurrentUser
{
    public Guid? UserId { get; } = userId;

    public string? Email { get; } = email;

    public string? TenantId { get; } = tenantId;

    public bool IsAuthenticated { get; } = isAuthenticated;
}
