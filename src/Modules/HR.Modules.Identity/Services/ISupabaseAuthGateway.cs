namespace HR.Modules.Identity.Services;

internal interface ISupabaseAuthGateway
{
    Task<Guid> CreateUserAsync(string email, string password, string redirectTo, CancellationToken cancellationToken);

    Task ResendVerificationEmailAsync(string email, string redirectTo, CancellationToken cancellationToken);

    Task RequestPasswordResetAsync(string email, string redirectTo, CancellationToken cancellationToken);

    Task<string> GenerateRecoveryLinkAsync(string email, string redirectTo, CancellationToken cancellationToken);

    Task UpdatePasswordAsync(string userAccessToken, string newPassword, CancellationToken cancellationToken);

    Task<SupabaseSession> ExchangeCodeForSessionAsync(string code, CancellationToken cancellationToken);

    Task<Guid> EnsureDevUserAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an already-confirmed (email_confirm: true) Supabase Auth user with the given
    /// <paramref name="password"/>, via the Admin API — the production (real-email, real-password)
    /// counterpart to EnsureDevUserAsync above. No email is sent: the caller (AcceptInvite) already
    /// has independent proof of email ownership — the invite link itself was emailed to that
    /// address — so there's nothing left to verify. Throws EmailAlreadyRegisteredException if
    /// Supabase reports the email as already registered (mirrors CreateUserAsync).
    ///
    /// Ticket 12 (P1): <paramref name="metadata"/>, when supplied, is stored as the new user's
    /// Supabase <c>user_metadata</c> — used by AcceptInvite to stamp an application-generated
    /// provisioning correlation value (the InviteAcceptanceOperation's own id) onto the account at
    /// creation time, so a later retry that hits EmailAlreadyRegisteredException can PROVE (via
    /// <see cref="GetUserMetadataByEmailAsync"/>) that this exact operation created the account,
    /// rather than assuming any "already registered" response on retry must mean that.
    /// </summary>
    Task<Guid> CreateConfirmedUserAsync(
        string email, string password, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? metadata = null);

    Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken);

    Task SignOutAsync(string userAccessToken, CancellationToken cancellationToken);

    Task<int> RemoveAllMfaFactorsAsync(Guid supabaseUserId, CancellationToken cancellationToken);

    Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Ticket 12 (P1): resolves the Supabase Auth user id AND <c>user_metadata</c> for
    /// <paramref name="email"/> (or <c>null</c> when no such user exists) — used by AcceptInvite to
    /// check whether a pre-existing/already-registered account carries the exact provisioning
    /// correlation value this invite's own InviteAcceptanceOperation stamped onto it at creation
    /// time (see <see cref="CreateConfirmedUserAsync"/>), before ever attaching a local profile to
    /// it.
    /// </summary>
    Task<(Guid UserId, IReadOnlyDictionary<string, string> Metadata)?> GetUserMetadataByEmailAsync(
        string email, CancellationToken cancellationToken);

    /// <summary>
    /// P1 platform-administrator provisioning: creates a pending (unverified) Supabase Auth user
    /// with a random, never-returned/never-stored password, stamps <paramref name="metadata"/> onto
    /// its <c>user_metadata</c> (the same anti-duplicate-account correlation pattern as
    /// <see cref="CreateConfirmedUserAsync"/>/Ticket 12), and sends the account-confirmation email
    /// whose link redirects to <paramref name="redirectTo"/>. Throws
    /// <see cref="EmailAlreadyRegisteredException"/> if Supabase reports the email as already
    /// registered (mirrors <see cref="CreateUserAsync"/>).
    /// </summary>
    Task<Guid> CreatePendingUserWithMetadataAsync(
        string email, string redirectTo, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken);
}

internal sealed record SupabaseSession(string AccessToken, string RefreshToken, Guid UserId, DateTimeOffset ExpiresAt);
