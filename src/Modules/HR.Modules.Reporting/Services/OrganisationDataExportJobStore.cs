using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Services;

/// <summary>
/// Story 2: Reporting-owned implementation of <see cref="IOrganisationDataExportJobStore"/> used by
/// the Infrastructure background jobs to advance a single export row through its lifecycle without
/// referencing <see cref="ReportingDbContext"/> directly.
/// </summary>
internal sealed class OrganisationDataExportJobStore(ReportingDbContext db, IClock clock)
    : IOrganisationDataExportJobStore
{
    public async Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var entity = await db.OrganisationDataExports
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == exportId, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return;

        entity.MarkInProgress(clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(Guid exportId, string storageKey, long fileSizeBytes, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return;

        entity.MarkCompleted(storageKey, fileSizeBytes, clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return;

        entity.MarkFailed(failureReason, clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var rows = await db.OrganisationDataExports
            .AsNoTracking()
            .Where(e => e.Status == OrganisationDataExport.StatusCompleted
                        && e.ExpiresAt != null
                        && e.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return;

        entity.MarkExpired(clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<OrganisationDataExport?> Load(Guid exportId, CancellationToken cancellationToken) =>
        db.OrganisationDataExports.SingleOrDefaultAsync(e => e.Id == exportId, cancellationToken);

    private static OrganisationDataExportJobView Map(OrganisationDataExport e) =>
        new(e.Id, e.CompanyId, e.Status, e.StorageKey, e.ExpiresAt);
}
