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

    public async Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return false;

        var expectedVersion = entity.Version;
        var begin = entity.BeginAttempt(ownerToken, clock.UtcNowOffset());
        if (begin.IsFailure)
        {
            db.ChangeTracker.Clear();
            return false;
        }

        var save = await db.SaveChangesWithConcurrencyAsync(
            entity,
            expectedVersion,
            $"Organisation data export '{exportId}' is already being processed.",
            cancellationToken);

        if (save.IsFailure)
            db.ChangeTracker.Clear();

        return save.IsSuccess;
    }

    public async Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return false;

        var expectedVersion = entity.Version;
        var renew = entity.RenewLease(ownerToken, clock.UtcNowOffset());
        if (renew.IsFailure)
        {
            db.ChangeTracker.Clear();
            return false;
        }

        var save = await db.SaveChangesWithConcurrencyAsync(
            entity,
            expectedVersion,
            $"Organisation data export '{exportId}' lease was taken by another worker.",
            cancellationToken);

        if (save.IsFailure)
            db.ChangeTracker.Clear();

        return save.IsSuccess;
    }

    public Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken) =>
        ApplyOwnedTransitionAsync(
            exportId,
            e => e.MarkFailedDueToMissingDocuments(ownerToken, missingCount, clock.UtcNowOffset()),
            cancellationToken);

    public async Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return;

        entity.ResetForRetry(clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return false;

        var expectedVersion = entity.Version;
        var claim = entity.ClaimForRecovery(recoveryToken, clock.UtcNowOffset());
        if (claim.IsFailure)
        {
            db.ChangeTracker.Clear();
            return false;
        }

        var save = await db.SaveChangesWithConcurrencyAsync(
            entity,
            expectedVersion,
            $"Organisation data export '{exportId}' recovery lost a race to another worker.",
            cancellationToken);

        if (save.IsFailure)
            db.ChangeTracker.Clear();

        return save.IsSuccess;
    }

    public Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
        ApplyOwnedTransitionAsync(
            exportId,
            e => e.ResetForRetry(recoveryToken, clock.UtcNowOffset()),
            cancellationToken);

    public async Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        // Ticket 3K: durable cursor. A candidate is due when it has never been deferred
        // (ArtefactCleanupNextAttemptAt == null) or its backoff has elapsed. A blocked entry (legal
        // hold, delete failure) is pushed forward and yields its slot, so older eligible exports are
        // always reached. Order NULLS FIRST on the cursor, then oldest completion, then id tie-break.
        var rows = await db.OrganisationDataExports
            .AsNoTracking()
            .Where(e => (e.Status == OrganisationDataExport.StatusFailed
                         || e.Status == OrganisationDataExport.StatusExpired
                         || e.Status == OrganisationDataExport.StatusCompleted)
                        && e.AttemptFilesCleanedAt == null
                        && (e.ArtefactCleanupNextAttemptAt == null || e.ArtefactCleanupNextAttemptAt <= now))
            .OrderBy(e => e.ArtefactCleanupNextAttemptAt != null)
            .ThenBy(e => e.ArtefactCleanupNextAttemptAt)
            .ThenBy(e => e.CompletedAt)
            .ThenBy(e => e.Id)
            .Take(batchSize <= 0 ? 1 : batchSize)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(
        int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var windowStart = now.AddDays(-OrganisationDataExport.LateUploadRecheckWindowDays);

        // Ticket 3K: durable straggler-recheck cursor. Eligible while still inside the 14-day window OR
        // while a prior recheck left a pending cursor (a failed recheck must not be dropped merely
        // because AttemptFilesCleanedAt aged past the window). Only picked when the cursor is due.
        // Order NULLS FIRST on the cursor so freshly-cleaned entries are rechecked before those already
        // rotated through, then by cleaned time, then id tie-break.
        var rows = await db.OrganisationDataExports
            .AsNoTracking()
            .Where(e => (e.Status == OrganisationDataExport.StatusFailed
                         || e.Status == OrganisationDataExport.StatusExpired
                         || e.Status == OrganisationDataExport.StatusCompleted)
                        && e.AttemptFilesCleanedAt != null
                        && (e.AttemptFilesCleanedAt >= windowStart || e.LateUploadRecheckNextAt != null)
                        && (e.LateUploadRecheckNextAt == null || e.LateUploadRecheckNextAt <= now))
            .OrderBy(e => e.LateUploadRecheckNextAt != null)
            .ThenBy(e => e.LateUploadRecheckNextAt)
            .ThenBy(e => e.AttemptFilesCleanedAt)
            .ThenBy(e => e.Id)
            .Take(batchSize <= 0 ? 1 : batchSize)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task MarkAttemptFilesCleanedAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return;

        var result = entity.MarkAttemptFilesCleaned(clock.UtcNowOffset());
        if (result.IsFailure)
            return;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeferArtefactCleanupAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return;

        if (entity.DeferArtefactCleanup(clock.UtcNowOffset()).IsFailure)
            return;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordLateUploadRecheckAsync(Guid exportId, bool succeeded, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return;

        if (entity.RecordLateUploadRecheck(succeeded, clock.UtcNowOffset()).IsFailure)
            return;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(
        DateTimeOffset pendingQueuedBefore,
        DateTimeOffset leaseExpiredAsOf,
        CancellationToken cancellationToken)
    {
        var rows = await db.OrganisationDataExports
            .AsNoTracking()
            .Where(e =>
                (e.Status == OrganisationDataExport.StatusPending && e.RequestedAt < pendingQueuedBefore)
                || (e.Status == OrganisationDataExport.StatusInProgress
                    && (e.LeaseExpiresAt == null || e.LeaseExpiresAt <= leaseExpiredAsOf)))
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken) =>
        ApplyOwnedTransitionAsync(
            exportId,
            e => e.MarkCompleted(ownerToken, storageKey, fileSizeBytes, clock.UtcNowOffset()),
            cancellationToken);

    public async Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken)
    {
        var entity = await Load(exportId, cancellationToken);
        if (entity is null)
            return;

        var expectedVersion = entity.Version;
        if (entity.MarkFailed(failureReason, clock.UtcNowOffset()).IsFailure)
        {
            db.ChangeTracker.Clear();
            return;
        }

        var save = await db.SaveChangesWithConcurrencyAsync(
            entity, expectedVersion,
            $"Organisation data export '{exportId}' changed concurrently.", cancellationToken);
        if (save.IsFailure)
            db.ChangeTracker.Clear();
    }

    public Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken) =>
        ApplyOwnedTransitionAsync(
            exportId,
            e => e.MarkFailed(ownerToken, failureReason, clock.UtcNowOffset()),
            cancellationToken);

    /// <summary>
    /// Ticket 3J: run an ownership-guarded terminal/recovery transition against a <b>fresh</b> tracked
    /// row and persist it under the optimistic-concurrency token. A benign heartbeat (same owner,
    /// <see cref="OrganisationDataExport.RenewLease"/>) landing between the reload and the save bumps
    /// the version and fails the guarded save; we clear the poisoned tracked changes, re-read and
    /// re-apply, keyed on lease-owner stability. A genuine takeover makes <paramref name="transition"/>
    /// return failure and we no-op. Bounded to a few iterations.
    /// </summary>
    private async Task<bool> ApplyOwnedTransitionAsync(
        Guid exportId,
        Func<OrganisationDataExport, Result> transition,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var entity = await Load(exportId, cancellationToken);
            if (entity is null)
                return false;

            var expectedVersion = entity.Version;
            if (transition(entity).IsFailure)
            {
                db.ChangeTracker.Clear();
                return false;
            }

            var save = await db.SaveChangesWithConcurrencyAsync(
                entity, expectedVersion,
                $"Organisation data export '{exportId}' changed concurrently.", cancellationToken);

            if (save.IsSuccess)
                return true;

            // Concurrency conflict — a heartbeat (or other write) landed first. Drop the failed
            // tracked changes so they cannot poison the retry, then reload and re-evaluate ownership.
            db.ChangeTracker.Clear();
        }

        return false;
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

    private Task<OrganisationDataExport?> Load(Guid exportId, CancellationToken cancellationToken)
    {
        // Ticket 3J: every write path starts from a genuinely fresh tracked row. Clearing first drops
        // any entity left tracked (possibly with a failed, version-bumped change) by an earlier call
        // on this scoped store instance so it can never poison a later operation.
        db.ChangeTracker.Clear();
        return db.OrganisationDataExports.SingleOrDefaultAsync(e => e.Id == exportId, cancellationToken);
    }

    private static OrganisationDataExportJobView Map(OrganisationDataExport e) =>
        new(e.Id, e.CompanyId, e.Status, e.StorageKey, e.ExpiresAt,
            e.RequestedByUserId, e.AttemptCount, e.StartedAt, e.LastAttemptAt,
            e.LeaseOwnerToken, e.LeaseExpiresAt);
}
