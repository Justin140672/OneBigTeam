using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class SessionRevocationStore(IdentityDbContext dbContext) : ISessionRevocationStore
{
    public async Task RevokeAsync(Guid supabaseAuthUserId, DateTimeOffset revokedAt, CancellationToken cancellationToken)
    {
        var existing = await dbContext.SessionRevocations
            .FirstOrDefaultAsync(r => r.SupabaseAuthUserId == supabaseAuthUserId, cancellationToken);

        if (existing is null)
        {
            dbContext.SessionRevocations.Add(SessionRevocation.Create(supabaseAuthUserId, revokedAt));
        }
        else
        {
            existing.RevokeAsOf(revokedAt);
        }

        // Synchronous write, awaited before the caller (LogoutHandler) returns a response — the
        // revocation must be durably visible to every other app instance's next read BEFORE the
        // logging-out tab's own request completes, closing the race where a concurrent request from
        // another tab could otherwise still be validated using the stale token.
        await dbContext.SaveChangesAsync(cancellationToken);
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
