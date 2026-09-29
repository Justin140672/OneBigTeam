using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Jobs;

internal sealed class PurgeOrphanedImportFileUploadsJob(
    DataImportDbContext db,
    IImportFileStorageService storage,
    IOptions<DataImportFileRetentionOptions> options,
    ILegalHoldStatusReader legalHoldStatusReader,
    IAdministrativeAlertWriter administrativeAlertWriter,
    IClock clock,
    ILogger<PurgeOrphanedImportFileUploadsJob> logger)
{
    private const int BatchSize = 200;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await ResolveUnconfirmedIntentsAsync(cancellationToken);
        await DeleteConfirmedOrphansAsync(cancellationToken);
    }

    private async Task ResolveUnconfirmedIntentsAsync(CancellationToken cancellationToken)
    {
        var retention = options.Value;
        var now = clock.UtcNowOffset();
        var graceCutoff = now.AddMinutes(-retention.UploadIntentGracePeriodMinutes);

        var unconfirmed = await db.OrphanedImportFileUploads
            .Where(o => o.ConfirmedAt == null
                && o.DeletedAt == null
                && o.ClearedAt == null
                && o.CreatedAt <= graceCutoff)
            .OrderBy(o => o.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var intent in unconfirmed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await legalHoldStatusReader.IsUnderLegalHoldAsync(intent.CompanyId, cancellationToken))
            {
                logger.LogInformation(
                    "Skipping unresolved upload-intent reconciliation for {IntentId}: company {CompanyId} is under a legal hold.",
                    intent.Id, intent.CompanyId);
                continue;
            }

            bool exists;
            try
            {
                exists = await storage.ExistsAsync(intent.StorageKey, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to check storage existence for unresolved upload intent {IntentId} (company {CompanyId}); will retry on the next sweep.",
                    intent.Id, intent.CompanyId);
                continue;
            }

            if (exists)
            {
                intent.MarkDeletionEligible(now);
                logger.LogWarning(
                    "PurgeOrphanedImportFileUploadsJob: unresolved upload intent {IntentId} (company {CompanyId}) has a blob in storage with no confirming session — will be deleted this sweep.",
                    intent.Id, intent.CompanyId);
                continue;
            }

            intent.MarkClearedNeverUploaded(now);
            logger.LogInformation(
                "PurgeOrphanedImportFileUploadsJob: cleared unresolved upload intent {IntentId} (company {CompanyId}) — no object was ever uploaded to storage.",
                intent.Id, intent.CompanyId);
        }

        if (unconfirmed.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteConfirmedOrphansAsync(CancellationToken cancellationToken)
    {
        var retention = options.Value;
        var now = clock.UtcNowOffset();
        var retryCutoff = now.AddHours(-retention.RetryGraceHours);

        try
        {
            var candidates = await db.OrphanedImportFileUploads
                .Where(o => o.DeletionEligibleAt != null
                    && o.ConfirmedAt == null
                    && o.ClearedAt == null
                    && o.DeletedAt == null
                    && (o.LastAttemptedAt == null || o.LastAttemptedAt <= retryCutoff))
                .OrderBy(o => o.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            foreach (var orphan in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await legalHoldStatusReader.IsUnderLegalHoldAsync(orphan.CompanyId, cancellationToken))
                {
                    logger.LogInformation(
                        "Skipping orphaned import file purge for {OrphanId}: company {CompanyId} is under a legal hold.",
                        orphan.Id, orphan.CompanyId);
                    continue;
                }

                orphan.BeginDeletionAttempt(now);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    db.ChangeTracker.Clear();
                    var current = await db.OrphanedImportFileUploads
                        .AsNoTracking()
                        .SingleOrDefaultAsync(o => o.Id == orphan.Id, cancellationToken);

                    if (current?.ConfirmedAt is not null)
                    {
                        logger.LogInformation(
                            "Orphaned import file upload {OrphanId} (company {CompanyId}) was confirmed by a request that raced ahead of this reconciliation sweep — skipping deletion, storage was never touched.",
                            orphan.Id, orphan.CompanyId);
                    }
                    else
                    {
                        logger.LogInformation(
                            "Orphaned import file upload {OrphanId} (company {CompanyId}) changed concurrently before this sweep could claim it; will retry next sweep.",
                            orphan.Id, orphan.CompanyId);
                    }

                    continue;
                }

                try
                {
                    await storage.DeleteAsync(orphan.StorageKey, cancellationToken);
                    orphan.MarkDeleted(now);
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    orphan.RecordAttemptFailed(now);
                    await db.SaveChangesAsync(cancellationToken);

                    if (orphan.AttemptCount >= retention.ExhaustedAttemptThreshold)
                    {
                        logger.LogError(ex,
                            "Orphaned import file upload {OrphanId} (company {CompanyId}) has failed deletion {AttemptCount} times and has exhausted its retry threshold; manual investigation required.",
                            orphan.Id, orphan.CompanyId, orphan.AttemptCount);

                        try
                        {
                            await administrativeAlertWriter.RaiseAsync(new RaiseAdministrativeAlertCommand(
                                CompanyId: orphan.CompanyId,
                                Severity: AdministrativeAlertSeverity.Warning,
                                Category: AdministrativeAlertCategory.Compliance,
                                Summary: "Orphaned import file upload could not be deleted after repeated attempts",
                                Detail: $"Orphaned import file upload {orphan.Id} has failed deletion {orphan.AttemptCount} times and has exhausted its retry threshold. The file may remain in storage indefinitely.",
                                OccurredAt: now,
                                DedupKey: $"compliance:dataimport-orphan-purge-exhausted:{orphan.Id}",
                                AffectedEntityType: "OrphanedImportFileUpload",
                                AffectedEntityId: orphan.Id,
                                RecommendedAction: "Investigate the storage backend and, if needed, delete the file manually.",
                                ActionUrl: null), cancellationToken);
                        }
                        catch (Exception alertEx)
                        {
                            logger.LogWarning(alertEx,
                                "Failed to raise exhausted-attempts alert for orphaned import file upload {OrphanId}.", orphan.Id);
                        }
                    }
                    else
                    {
                        logger.LogWarning(ex,
                            "Failed to delete orphaned import file upload {OrphanId} (company {CompanyId}); attempt {AttemptCount}, will retry.",
                            orphan.Id, orphan.CompanyId, orphan.AttemptCount);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "PurgeOrphanedImportFileUploadsJob failed.");
            throw;
        }
    }
}
