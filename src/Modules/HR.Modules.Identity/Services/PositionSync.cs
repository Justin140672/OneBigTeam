using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

/// <summary>
/// IAM-03: keeps identity.positions in sync with HR.Modules.Employees' PositionProfile, without a
/// direct module reference or database join. Identity's <see cref="Position"/> row is keyed
/// directly by the owning PositionProfile's Id (see Position.Create remarks) and is a lazily
/// synced, role-administration-only projection — the only coupling to Employees is the
/// IPositionProfileReader contract (a company-scoped read) and the integration events that trigger
/// a sync (EmployeeCreatedIntegrationEvent, EmployeePositionChangedIntegrationEvent).
///
/// Known limitation (documented, not fixed here): if a PositionProfile's Title is renamed in the
/// Employees module, the mirrored identity.positions.name is not automatically refreshed until the
/// next event that calls EnsureExistsAsync for that position (e.g. an employee assignment change)
/// or an administrator opens the "configure default roles" screen (ListPositionRoleDefaults refreshes
/// the name on every read). There is no dedicated PositionProfileRenamed integration event today;
/// adding one is out of scope for IAM-03.
/// </summary>
internal sealed class PositionSync(IdentityDbContext db, IPositionProfileReader positionProfileReader)
{
    /// <summary>
    /// Ensures an identity.positions row exists for the given PositionProfile, scoped to
    /// <paramref name="companyId"/>. Returns null if the PositionProfile can no longer be resolved
    /// for that company (e.g. it belongs to a different company, or has been deleted) — callers
    /// must treat that as "nothing to sync", not as an error, since integration event delivery can
    /// race a profile's own lifecycle.
    /// </summary>
    public async Task<Position?> EnsureExistsAsync(
        Guid companyId, Guid positionProfileId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // FindAsync (not a query): checks the context's already-tracked entities before hitting the
        // DB. The reconciliation loop calls this once per employee, and several employees can share
        // one PositionProfile — a plain query would miss the row this loop just Added-but-not-yet-
        // saved for an earlier employee and Add() it a second time, throwing an identity conflict
        // on the duplicate key. Position.Id IS the PositionProfile id, so a key lookup is exact.
        var existing = await db.Positions.FindAsync([positionProfileId], cancellationToken);

        var summary = await positionProfileReader.GetSummaryAsync(companyId, positionProfileId, cancellationToken);
        if (summary is null)
            return existing; // Can't confirm company ownership right now — leave any existing row untouched.

        if (existing is null)
        {
            var created = Position.Create(positionProfileId, companyId, summary.Title, now);
            db.Positions.Add(created);

            // Persist the row now, tolerating a concurrent creator. identity.positions is a lazy
            // projection created from several unsynchronised triggers — an employee-created /
            // position-changed integration event, the startup reconciliation loop, and an admin
            // opening the role-defaults screen — which can race to insert the same key. Without
            // this, the loser's later SaveChangesAsync (e.g. SetPositionRoleDefaultsHandler's
            // combined position + position_roles insert) blew up with a duplicate-key
            // DbUpdateException and 500'd the request.
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return created;
            }
            catch (DbUpdateException)
            {
                db.Entry(created).State = EntityState.Detached;
                return await db.Positions.FindAsync([positionProfileId], cancellationToken);
            }
        }

        if (!string.Equals(existing.Name, summary.Title, StringComparison.Ordinal))
            existing.Rename(summary.Title, now);

        if (summary.IsActive && !existing.IsActive)
            existing.Reactivate(now);
        else if (!summary.IsActive && existing.IsActive)
            existing.Deactivate(now);

        return existing;
    }
}
