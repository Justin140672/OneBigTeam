using HR.SharedKernel;

namespace HR.Modules.Marketing.Tests.Infrastructure;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public FakeCurrentUser(Guid? userId, string? email = "admin@example.com", string? tenantId = null, bool isAuthenticated = true)
    {
        UserId = userId;
        Email = email;
        TenantId = tenantId;
        IsAuthenticated = isAuthenticated;
    }

    public Guid? UserId { get; }

    public string? Email { get; }

    public string? TenantId { get; }

    public bool IsAuthenticated { get; }
}
