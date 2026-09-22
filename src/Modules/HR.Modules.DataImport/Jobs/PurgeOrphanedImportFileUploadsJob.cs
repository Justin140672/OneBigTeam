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

/// <summary>
/// Follow-up review finding: this sweep now has two responsibilities, both driven by the same
/// <see cref="OrphanedImportFileUpload"/> table:
///
///  1. <see cref="ResolveUnconfirmedIntentsAsync"/> — resolves durable pre-upload "upload intent"
///     rows (<c>ConfirmedAt</c> still null) that have sat unresolved past the configured grace
///     period. These rows are written BEFORE the storage upload call in
///     Features/UploadImportFile/Handler.cs — see that type's remarks — so this sweep is the
///     authoritative backstop for every failure mode: an upload that never completed, a session
///     save that failed after a successful upload, or a process crash at any point in between,
///     including a persistent database outage that prevented every write after the initial intent.
///     It checks whether the object actually exists in storage: if it does, the row is treated
///     exactly like a confirmed orphan (falls through to the deletion loop below); if it does not
///     (the process crashed before the upload itself completed), the intent is simply cleared —
///     there was never anything to delete.
///  2. The pre-existing deletion loop — idempotent deletion, per-company legal-hold skip, retry
///     grace window and an exhausted-attempts alert — for rows already known to need a delete
///     (either a resolved intent found to have an object in storage, or a legacy compensation
///     write from before this change).
///
/// Both reuse <see cref="DataImportFileRetentionOptions"/> so no new configuration surface is
/// introduced beyond <see cref="DataImportFileRetentionOptions.UploadIntentGracePeriodMinutes"/>.
/// "Log an unrecoverable orphan and give up" is no longer a terminal outcome anywhere in this
/// flow — every failure path leaves a durable, reconciliable row that this sweep keeps retrying.
/// </summary>
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

    /// <summary>
    /// Resolves durable pre-upload intents that were never confirmed within the grace period —
    /// see class remarks. Never deletes anything directly: an intent found to have an object in
    /// storage is left for <see cref="DeleteConfirmedOrphansAsync"/> to actually delete (on this
    /// same sweep, since it runs immediately afterward), keeping exactly one code path responsible
    /// for the destructive delete + retry/alert logic.
    /// </summary>
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
                // Leave DeletedAt/ClearedAt/ConfirmedAt untouched — the object genuinely exists and
                // must be deleted. Falling through to the deletion loop below (same sweep) picks it
                // up via the unchanged eligibility query there (ConfirmedAt/DeletedAt/ClearedAt all
                // still null).
                logger.LogWarning(
                    "PurgeOrphanedImportFileUploadsJob: unresolved upload intent {IntentId} (company {CompanyId}) has a blob in storage with no confirming session — will be deleted this sweep.",
                    intent.Id, intent.CompanyId);
                continue;
            }

            // The process crashed before the upload itself completed — there is nothing to delete.
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
                .Where(o => o.ConfirmedAt == null
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
