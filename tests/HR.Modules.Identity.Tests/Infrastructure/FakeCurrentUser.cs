using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public FakeCurrentUser(Guid? userId, string? email = null, string? tenantId = null, bool isAuthenticated = true)
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

    public static FakeCurrentUser Authenticated(Guid userId, string? tenantId = null) =>
        new(userId, "test@example.com", tenantId, isAuthenticated: true);

    public static FakeCurrentUser Anonymous =>
        new(null, null, null, isAuthenticated: false);
}
