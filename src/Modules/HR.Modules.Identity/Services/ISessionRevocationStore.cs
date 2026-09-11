namespace HR.Modules.Identity.Services;

/// <summary>
/// Ticket 13: server-side, cross-replica enforcement of "this Supabase user's tokens issued before
/// time T must no longer be honoured", independent of the token's own (unexpired) lifetime and of
/// Supabase's own (delayed/best-effort) sign-out call. Backed by a table in this module's own
/// PostgreSQL schema (the shared Postgres database this whole platform already depends on — see
/// 08-deployment-architecture.md), so every application instance/replica sees the same revocation
/// state on its very next read; no new infrastructure (e.g. Redis) is introduced.
/// </summary>
internal interface ISessionRevocationStore
{
    /// <summary>
    /// Records that every token issued to <paramref name="supabaseAuthUserId"/> before
    /// <paramref name="revokedAt"/> must be rejected from now on. Idempotent and safe under
    /// concurrent calls: an existing, later revocation instant is never moved backward.
    /// </summary>
    Task RevokeAsync(Guid supabaseAuthUserId, DateTimeOffset revokedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the instant this user's sessions were last revoked, or null if they have never been
    /// revoked (or the revocation record does not exist).
    /// </summary>
    Task<DateTimeOffset?> GetRevokedAtAsync(Guid supabaseAuthUserId, CancellationToken cancellationToken);
}
