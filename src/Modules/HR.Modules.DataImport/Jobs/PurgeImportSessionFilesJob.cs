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

internal sealed class PurgeImportSessionFilesJob(
    DataImportDbContext db,
    IImportFileStorageService storage,
    IOptions<DataImportFileRetentionOptions> options,
    ILegalHoldStatusReader legalHoldStatusReader,
    IAdministrativeAlertWriter administrativeAlertWriter,
    IClock clock,
    ILogger<PurgeImportSessionFilesJob> logger)
{
    // Terminal states after which the raw file is never needed again by the workflow. Processing
    // is intentionally excluded here — it is only treated as abandoned via the separate
    // Pending/Processing branch below, using UpdatedAt rather than CompletedAt, since a session
    // actively being confirmed does not set CompletedAt until Confirm() runs.
    private static readonly ImportStatus[] TerminalStatuses =
    [
        ImportStatus.Completed,
        ImportStatus.CompletedWithErrors,
        ImportStatus.Failed,
        ImportStatus.Cancelled,
        ImportStatus.Validated,
        ImportStatus.Imported,
    ];

    private const int BatchSize = 200;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var retention = options.Value;
        var now = clock.UtcNowOffset();
        var retryCutoff = now.AddHours(-retention.RetryGraceHours);
        var abandonedCutoff = now.AddDays(-retention.AbandonedSessionRetentionDays);

        try
        {
            var candidates = await db.ImportSessions
                .Where(s => s.FileDeletedAt == null
                    && s.StorageKey != ""
                    && (s.FileDeletionLastAttemptedAt == null || s.FileDeletionLastAttemptedAt <= retryCutoff)
                    && (
                        (TerminalStatuses.Contains(s.Status) && s.CompletedAt != null && s.CompletedAt <= retryCutoff)
                        || ((s.Status == ImportStatus.Pending || s.Status == ImportStatus.Processing) && s.UpdatedAt <= abandonedCutoff)
                    ))
                .OrderBy(s => s.UpdatedAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            foreach (var session in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await legalHoldStatusReader.IsUnderLegalHoldAsync(session.CompanyId, cancellationToken))
                {
                    logger.LogInformation(
                        "Skipping raw-file purge for import session {ImportSessionId}: company {CompanyId} is under a legal hold.",
                        session.Id, session.CompanyId);
                    continue;
                }

                try
                {
                    await storage.DeleteAsync(session.StorageKey, cancellationToken);
                    session.MarkFileDeleted(now);
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    session.RecordFileDeletionAttemptFailed(now);
                    await db.SaveChangesAsync(cancellationToken);

                    if (session.FileDeletionAttemptCount >= retention.ExhaustedAttemptThreshold)
                    {
                        logger.LogError(ex,
                            "Import session {ImportSessionId} raw file (storage key {StorageKey}) has failed deletion {AttemptCount} times and has exhausted its retry threshold; manual investigation required.",
                            session.Id, session.StorageKey, session.FileDeletionAttemptCount);

                        try
                        {
                            await administrativeAlertWriter.RaiseAsync(new RaiseAdministrativeAlertCommand(
                                CompanyId: session.CompanyId,
                                Severity: AdministrativeAlertSeverity.Warning,
                                Category: AdministrativeAlertCategory.Compliance,
                                Summary: "Import session raw file could not be deleted after repeated attempts",
                                Detail: $"Import session {session.Id} has failed raw-file deletion {session.FileDeletionAttemptCount} times and has exhausted its retry threshold. The file may remain in storage past its intended retention window.",
                                OccurredAt: now,
                                DedupKey: $"compliance:dataimport-file-purge-exhausted:{session.Id}",
                                AffectedEntityType: "ImportSession",
                                AffectedEntityId: session.Id,
                                RecommendedAction: "Investigate the storage backend and, if needed, delete the file manually, then clear the session's deletion-attempt state.",
                                ActionUrl: null), cancellationToken);
                        }
                        catch (Exception alertEx)
                        {
                            logger.LogWarning(alertEx,
                                "Failed to raise exhausted-attempts alert for import session {ImportSessionId}.", session.Id);
                        }
                    }
                    else
                    {
                        logger.LogWarning(ex,
                            "Failed to delete import session {ImportSessionId} raw file (storage key {StorageKey}); attempt {AttemptCount}, will retry.",
                            session.Id, session.StorageKey, session.FileDeletionAttemptCount);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "PurgeImportSessionFilesJob failed.");
            throw;
        }
    }
}
