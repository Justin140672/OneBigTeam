namespace HR.Modules.Identity.Services;

// Mirrors HR.Modules.Companies.Services.IStripeGateway's shape/intent: an internal, narrow,
// HTTP-based gateway to a third-party Auth Admin API, kept out of the handler so it's mockable in
// tests (see tests/HR.Modules.Identity.Tests for the fake implementation).
internal interface ISupabaseAuthGateway
{
    /// <summary>
    /// Creates a pending (unverified) Supabase Auth user and sends a verification email whose link
    /// redirects to <paramref name="redirectTo"/>. Returns the Supabase Auth user id.
    /// </summary>
    Task<Guid> CreateUserAsync(string email, string redirectTo, CancellationToken cancellationToken);

    /// <summary>
    /// Resends the verification email for a still-pending user.
    /// </summary>
    Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a password-recovery email whose link redirects to <paramref name="redirectTo"/>.
    /// Confirmed via live testing (see HR.Web's /verify-email remarks): Supabase uses the
    /// implicit/fragment flow for this redirect, the same as email verification —
    /// "{redirectTo}#access_token=...&amp;type=recovery" — not a "code" query parameter.
    /// </summary>
    Task RequestPasswordResetAsync(string email, string redirectTo, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a new password for the user identified by <paramref name="userAccessToken"/> — the
    /// short-lived access token Supabase issues via the password-recovery redirect's fragment
    /// (see RequestPasswordResetAsync). Calls Supabase's user-scoped PUT /auth/v1/user, which
    /// authenticates via that token directly rather than the publishable/secret API keys used
    /// elsewhere in this gateway.
    /// </summary>
    Task UpdatePasswordAsync(string userAccessToken, string newPassword, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges the "code" query parameter from Supabase's verification redirect for a real
    /// Supabase session (access/refresh tokens).
    /// </summary>
    Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently ensures a confirmed (email_confirm: true) Supabase Auth user exists for a
    /// Development dev-persona, via the Admin API (POST /auth/v1/admin/users). Unlike
    /// CreateUserAsync, this never sends an invite/verification email — it is purely for seeding a
    /// login-ready dev user. Safe to call repeatedly: Supabase's "already exists" error response is
    /// treated as success, falling back to an admin user lookup by email so the Supabase Auth user
    /// id is always returned — callers need a stable id to link a UserProfile row to, regardless of
    /// whether this call created the user or found it already existing.
    /// </summary>
    Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Performs a real Supabase password-grant login (POST /auth/v1/token?grant_type=password) for
    /// a Development dev-persona, used by the dev persona switcher to establish a genuine Supabase
    /// session.
    /// </summary>
    Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken);
}

internal sealed record SupabaseSession(string AccessToken, string RefreshToken, Guid UserId, DateTimeOffset ExpiresAt);
