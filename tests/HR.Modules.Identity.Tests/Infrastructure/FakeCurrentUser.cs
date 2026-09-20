using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public FakeCurrentUser(
        Guid? userId,
        string? email = null,
        string? tenantId = null,
        bool isAuthenticated = true,
        bool isSupportSession = false,
        Guid? supportSessionId = null)
    {
        UserId = userId;
        Email = email;
        TenantId = tenantId;
        IsAuthenticated = isAuthenticated;
        IsSupportSession = isSupportSession;
        SupportSessionId = supportSessionId;
    }

    public Guid? UserId { get; }

    public string? Email { get; }

    public string? TenantId { get; }

    public bool IsAuthenticated { get; }

    public bool IsSupportSession { get; }

    public Guid? SupportSessionId { get; }

    public static FakeCurrentUser Authenticated(Guid userId, string? tenantId = null) =>
        new(userId, "test@example.com", tenantId, isAuthenticated: true);

    public static FakeCurrentUser Anonymous =>
        new(null, null, null, isAuthenticated: false);

    /// <summary>
    /// P1 "Login as Customer": a resolved support-session caller — carries the acting admin's real
    /// email (for display/audit) but must never be treated as that admin's real authenticated
    /// identity by any authorization handler.
    /// </summary>
    public static FakeCurrentUser SupportSession(Guid userId, string? email = null, string? tenantId = null, Guid? supportSessionId = null) =>
        new(userId, email ?? "admin@example.com", tenantId, isAuthenticated: true, isSupportSession: true, supportSessionId: supportSessionId ?? Guid.NewGuid());
}
