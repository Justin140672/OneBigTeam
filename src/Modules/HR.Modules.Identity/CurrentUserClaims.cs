namespace HR.Modules.Identity;

internal static class CurrentUserClaims
{
    public const string SupabaseUserId = "sub";
    public const string Email = "email";
    public const string TenantId = "company_id";

    public const string SupportSessionId = "support_session_id";
}