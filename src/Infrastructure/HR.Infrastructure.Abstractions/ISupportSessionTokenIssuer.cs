namespace HR.Infrastructure.Abstractions;

/// <summary>
/// P1 "Login as Customer": mints the short-lived, distinctly-signed access token a platform
/// administrator's browser uses after successfully redeeming a
/// HR.Modules.Companies.Domain.SupportSession. This is NOT a real Supabase-issued token — it is
/// signed with a dedicated symmetric key (config: SupportSession:SigningKey) and carries its own
/// issuer, so HR.Api's authentication pipeline can validate it through a distinct scheme and
/// SupabaseCurrentUserResolutionMiddleware can recognise it (support_session_id claim) and build a
/// support-scoped identity that is never resolved against identity.user_profiles.
/// </summary>
public interface ISupportSessionTokenIssuer
{
    /// <param name="supportSessionId">The persisted SupportSession row's id.</param>
    /// <param name="companyId">The target customer company the session is scoped to.</param>
    /// <param name="adminUserId">The acting platform administrator's own Supabase user id. Used
    /// (not a fabricated marker) as the token's "sub" claim so every audit event raised during the
    /// session — which reads its actor from ICurrentUser.UserId exactly like every other request —
    /// correctly attributes to the real administrator. Safe: SupabaseCurrentUserResolutionMiddleware
    /// never performs a UserProfile lookup for a support-session token (it short-circuits entirely
    /// on the support_session_id claim), so this can never resolve to a real employee identity
    /// even if the admin's Supabase user id happens to also be linked to a UserProfile somewhere.</param>
    /// <param name="adminEmail">The platform administrator's email, carried for display/audit only.</param>
    /// <param name="expiresAt">Must match the SupportSession's own ExpiresAt — the token must never
    /// outlive the server-side record that governs it.</param>
    string IssueToken(Guid supportSessionId, Guid companyId, Guid adminUserId, string adminEmail, DateTimeOffset expiresAt);
}
