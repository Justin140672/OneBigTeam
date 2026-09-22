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
/// Security review finding #2: durable, retryable sweep for raw import workbook files (PII:
/// names, emails, employment data, manager relationships) that were not deleted inline.
///
/// The immediate-deletion path lives in ValidateImportSessionHandler — the file is only ever
/// read there, so it is deleted the moment validation finishes (success or failure). This job is
/// the safety net for everything that path cannot cover:
/// <list type="bullet">
///   <item>Sessions abandoned before they were ever validated (stuck Pending/Processing) — swept
///   once they are older than <see cref="DataImportFileRetentionOptions.AbandonedSessionRetentionDays"/>.</item>
///   <item>Sessions that reached a terminal state without validation ever running (Cancelled) or
///   whose terminal state does not on its own imply the file was handled.</item>
///   <item>Any inline deletion attempt that failed (storage outage, transient error) — retried
///   here on a rolling basis, gated by <see cref="DataImportFileRetentionOptions.RetryGraceHours"/>
///   so failures do not get hammered every tick.</item>
/// </list>
///
/// Deletion is idempotent (see <see cref="IImportFileStorageService.DeleteAsync"/>) and status is
/// tracked on the session (FileDeletedAt / FileDeletionAttemptCount) separately from the
/// session's business Status, so a retry or a concurrent run can never double-process or corrupt
/// import progress. <see cref="DisableConcurrentExecutionAttribute"/> takes a distributed lock so
/// two scheduler ticks never race the same batch.
///
/// Unlike <c>PurgeExpiredReadNotificationsJob</c> this job does not ship in dry-run mode: the
/// finding this closes is that raw import files are <i>never</i> deleted today, so a dry-run
/// default would leave the vulnerability open. It does follow that job's other two conventions —
/// per-company legal-hold skip (<see cref="ILegalHoldStatusReader"/>) and a failure alert via
/// <see cref="IAdministrativeAlertWriter"/> — since a data-import raw file is customer HR content,
/// not transient UI state.
/// </summary>
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
                    // Legal hold preserves everything for the tenant, including transient
                    // workflow artefacts — the file is left untouched and will be reconsidered
                    // on a later run once the hold lifts. Not recorded as a failed attempt: this
                    // is an intentional skip, not an error.
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
