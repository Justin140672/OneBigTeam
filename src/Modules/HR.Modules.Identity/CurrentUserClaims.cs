namespace HR.Modules.Identity;

internal static class CurrentUserClaims
{
    public const string SupabaseUserId = "sub";
    public const string Email = "email";
    public const string TenantId = "company_id";

    /// <summary>
    /// Present only on a "Login as Customer" support-session token (see
    /// SupportSessionTokenAuthenticationExtensions / HR.Infrastructure's support-session JWT
    /// issuer). Its presence is what SupabaseCurrentUserResolutionMiddleware uses to build a
    /// support-scoped ResolvedCurrentUser instead of resolving a real UserProfile.
    /// </summary>
    public const string SupportSessionId = "support_session_id";
}