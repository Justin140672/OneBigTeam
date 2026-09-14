using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class SessionRevocationStore(IdentityDbContext dbContext) : ISessionRevocationStore
{
    public async Task RevokeAsync(Guid supabaseAuthUserId, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        // Ticket 14: a prior read-then-write implementation (load row, mutate in memory via
        // SessionRevocation.RevokeAsOf, SaveChangesAsync) had no database-level concurrency
        // protection. Two concurrent logout requests for the same user could each read the same
        // existing row; whichever one saved LAST would win outright, even if it carried an earlier
        // (stale) cutoff than the one that already committed — silently undoing an already-completed
        // revocation. The same gap allowed two concurrent first-time logouts for a previously-unseen
        // user to both attempt an INSERT and duplicate-key-fail.
        //
        // This is replaced with a single atomic PostgreSQL upsert: INSERT ... ON CONFLICT DO UPDATE
        // computing GREATEST(existing, incoming) server-side, inside one statement. Postgres
        // serialises concurrent upserts targeting the same conflict key — whichever transaction
        // commits second always sees the first transaction's already-committed row and recomputes
        // GREATEST against it, so there is no read-then-write gap regardless of process/replica/
        // interleaving, and no separate existence check is needed to avoid duplicate-key inserts.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO identity.session_revocations (supabase_auth_user_id, revoked_at)
             VALUES ({supabaseAuthUserId}, {revokedAt})
             ON CONFLICT (supabase_auth_user_id) DO UPDATE
             SET revoked_at = GREATEST(identity.session_revocations.revoked_at, EXCLUDED.revoked_at)
             """,
            cancellationToken);
    }

    public async Task<DateTimeOffset?> GetRevokedAtAsync(Guid supabaseAuthUserId, CancellationToken cancellationToken)
    {
        return await dbContext.SessionRevocations
            .AsNoTracking()
            .Where(r => r.SupabaseAuthUserId == supabaseAuthUserId)
            .Select(r => (DateTimeOffset?)r.RevokedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
