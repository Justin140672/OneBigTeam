using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Services;

internal sealed class PositionSync(IdentityDbContext db, IPositionProfileReader positionProfileReader)
{
    public async Task<Position?> EnsureExistsAsync(
        Guid companyId, Guid positionProfileId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Positions.FindAsync([positionProfileId], cancellationToken);

        var summary = await positionProfileReader.GetSummaryAsync(companyId, positionProfileId, cancellationToken);
        if (summary is null)
            return existing;

        if (existing is null)
        {
            var created = Position.Create(positionProfileId, companyId, summary.Title, now);
            db.Positions.Add(created);

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
