namespace HR.Modules.Identity;

internal sealed record ResolvedCurrentUser(
    Guid? UserId,
    string? Email,
    string? TenantId,
    bool IsAuthenticated);