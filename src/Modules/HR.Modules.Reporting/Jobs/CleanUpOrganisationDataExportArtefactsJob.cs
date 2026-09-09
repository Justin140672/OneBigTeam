using Hangfire;
using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Reporting.Jobs;

/// <summary>
/// Follow-up I: retryable, bounded cleanup of uploaded attempt archives left behind by failed or
/// abandoned organisation data exports — including archives whose upload succeeded but whose
/// completion never persisted (superseded worker, lost storage response).
///
/// Rules (shared with follow-ups G and H):
/// <list type="bullet">
///   <item>Only terminal exports are considered: Failed, Expired, Completed. Pending / InProgress
///   attempts are never touched.</item>
///   <item>The published archive (<c>StorageKey</c>) is never deleted — only orphan sibling attempt
///   objects under the same export folder.</item>
///   <item>Companies under a legal hold are skipped and retried on a later run.</item>
///   <item>A delete failure is logged and leaves the export un-marked so the next run retries; the
///   original export failure reason is never overwritten.</item>
///   <item>Bounded batches; repeated runs are idempotent.</item>
///   <item>A second bounded pass re-checks recently-cleaned exports so an archive uploaded by a
///   superseded worker <i>after</i> an earlier sweep is still removed.</item>
/// </list>
///
/// <para>Ticket 3K — durable fairness. Each pass loads at most <see cref="BatchSize"/> rows. Both the
/// candidate scan and the straggler recheck use a persisted per-row cursor
/// (<c>artefact_cleanup_next_attempt_at</c> / <c>late_upload_recheck_next_at</c>, id as tie-breaker).
/// A row that cannot be finished on a run — company under a legal hold, or a storage listing/delete
/// failure — is pushed behind a capped exponential backoff and <b>yields its batch slot</b>, so a
/// permanently blocked entry (legal hold, persistent delete failure) can never starve older eligible
/// exports: every eligible export is eventually attempted across repeated runs, and an interrupted or
/// restarted job resumes from the DB cursor without losing work. The late-upload recheck window is
/// <see cref="OrganisationDataExport.LateUploadRecheckWindowDays"/> days; a <i>failed</i> recheck keeps
/// its cursor set and stays retryable even after the row ages past that window, while a successful
/// recheck stops once the window closes.</para>
/// Scheduled from <see cref="ReportingModule.UseReportingRecurringJobs"/>.
/// </summary>
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 600)]
internal sealed class CleanUpOrganisationDataExportArtefactsJob(
    IOrganisationDataExportJobStore jobStore,
    IOrganisationDataExportStorage storage,
    ILegalHoldStatusReader legalHoldStatusReader,
    ILogger<CleanUpOrganisationDataExportArtefactsJob> logger)
{
    internal const int BatchSize = 50;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await jobStore.GetArtefactCleanupCandidatesAsync(BatchSize, cancellationToken);
        foreach (var export in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await legalHoldStatusReader.IsUnderLegalHoldAsync(export.CompanyId, cancellationToken))
            {
                logger.LogInformation(
                    "Skipping attempt-archive cleanup for organisation data export {ExportId}: company {CompanyId} is under a legal hold.",
                    export.Id, export.CompanyId);
                // Ticket 3K: yield the batch slot so unrelated companies still progress on this run.
                await jobStore.DeferArtefactCleanupAsync(export.Id, cancellationToken);
                continue;
            }

            if (await TryDeleteOrphanAttemptArchivesAsync(export, cancellationToken))
                await jobStore.MarkAttemptFilesCleanedAsync(export.Id, cancellationToken);
            else
                // Ticket 3K: listing/delete failure — stay retryable behind a backoff, yield the slot.
                await jobStore.DeferArtefactCleanupAsync(export.Id, cancellationToken);
        }

        // Straggler re-check: a superseded worker may have finished uploading after an earlier sweep.
        var recheck = await jobStore.GetRecentlyCleanedArtefactsAsync(BatchSize, cancellationToken);
        foreach (var export in recheck)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await legalHoldStatusReader.IsUnderLegalHoldAsync(export.CompanyId, cancellationToken))
            {
                // Ticket 3K: advance the cursor so a held company does not monopolise the recheck batch.
                await jobStore.RecordLateUploadRecheckAsync(export.Id, succeeded: false, cancellationToken);
                continue;
            }

            var cleaned = await TryDeleteOrphanAttemptArchivesAsync(export, cancellationToken);
            await jobStore.RecordLateUploadRecheckAsync(export.Id, cleaned, cancellationToken);
        }
    }

    /// <returns><c>true</c> only when every orphan attempt archive was confirmed removed.</returns>
    private async Task<bool> TryDeleteOrphanAttemptArchivesAsync(
        OrganisationDataExportJobView export, CancellationToken cancellationToken)
    {
        List<string> keys;
        try
        {
            keys = (await storage.ListAttemptKeysAsync(export.CompanyId, export.Id, cancellationToken)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Organisation data export {ExportId}: could not list attempt archives for cleanup; will retry next run.",
                export.Id);
            return false;
        }

        var allDeleted = true;
        foreach (var key in keys)
        {
            // Preserve the published archive.
            if (!string.IsNullOrWhiteSpace(export.StorageKey) &&
                string.Equals(key, export.StorageKey, StringComparison.Ordinal))
                continue;

            try
            {
                await storage.DeleteAsync(key, cancellationToken);
                logger.LogInformation(
                    "Organisation data export {ExportId}: deleted orphan attempt archive {StorageKey}.", export.Id, key);
            }
            catch (Exception ex)
            {
                allDeleted = false;
                logger.LogWarning(ex,
                    "Organisation data export {ExportId}: failed to delete orphan attempt archive {StorageKey}; will retry next run.",
                    export.Id, key);
            }
        }

        return allDeleted;
    }
}
