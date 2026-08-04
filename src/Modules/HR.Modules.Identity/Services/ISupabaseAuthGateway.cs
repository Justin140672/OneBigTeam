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
    /// Exchanges the "code" query parameter from Supabase's verification redirect for a real
    /// Supabase session (access/refresh tokens).
    /// </summary>
    Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken);
}

internal sealed record SupabaseSession(string AccessToken, string RefreshToken, Guid UserId, DateTimeOffset ExpiresAt);
