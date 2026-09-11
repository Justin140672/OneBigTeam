namespace HR.Modules.Identity.Domain;

// Ticket 13 (cross-tab logout enforcement): one row per Supabase Auth user who has ever logged out,
// recording the instant every access token issued to that user before "now" must be treated as
// revoked, regardless of the token's own (unexpired) lifetime claims. Keyed by the Supabase Auth
// user id (the JWT "sub" claim) rather than the local UserProfile/PlatformAdministrator id, because
// that id is the only stable identifier available BOTH at token-validation time (before any
// UserProfile lookup has happened — see SupabaseCurrentUserResolutionMiddleware, which runs after
// authentication) and for platform administrators, who have no UserProfile row at all.
//
// Scope model: per-user, not per-session. HR.Web/HR.Admin.Web only ever hold one Supabase session
// per browser tab and every tab logged in as the same user shares the same underlying Supabase
// identity; "log out tab A, tab B (same user) must also be rejected" is exactly a per-user
// revocation-timestamp model — there is no separate concept of a distinguishable "session id" this
// codebase currently tracks or would need to. A future per-device/per-session revocation model would
// need a session identifier minted at login and threaded through every token, which does not exist
// today and is out of scope for closing this gap.
internal sealed class SessionRevocation
{
    private SessionRevocation() { }

    public Guid SupabaseAuthUserId { get; private set; }
    public DateTimeOffset RevokedAt { get; private set; }

    public static SessionRevocation Create(Guid supabaseAuthUserId, DateTimeOffset revokedAt)
    {
        return new SessionRevocation
        {
            SupabaseAuthUserId = supabaseAuthUserId,
            RevokedAt = revokedAt,
        };
    }

    /// <summary>
    /// Moves the revocation instant forward. Never moves it backward: a slower, out-of-order write
    /// (e.g. two concurrent logout requests) must never un-revoke a session that a later logout
    /// already covered.
    /// </summary>
    public void RevokeAsOf(DateTimeOffset revokedAt)
    {
        if (revokedAt > RevokedAt)
        {
            RevokedAt = revokedAt;
        }
    }
}
