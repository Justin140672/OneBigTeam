namespace HR.SharedKernel;

public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Email { get; }

    string? TenantId { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// True when this request is authenticated as a platform-administrator "Login as Customer"
    /// support session (see HR.Modules.Companies.Domain.SupportSession) rather than a real
    /// employee/user identity. Support sessions are restricted to a narrow, read-only permission
    /// grant (see HR.Modules.Identity.Authorization.PermissionAuthorizationHandler) and can never
    /// resolve to a real EmployeeId, so they cannot satisfy employee-ownership authorization
    /// rules. Defaults to false so every existing <see cref="ICurrentUser"/> implementation is
    /// unaffected.
    /// </summary>
    bool IsSupportSession => false;

    /// <summary>
    /// The <see cref="HR.Modules.Companies.Domain.SupportSession"/> id this request is acting
    /// under, when <see cref="IsSupportSession"/> is true. Null otherwise.
    /// </summary>
    Guid? SupportSessionId => null;
}
