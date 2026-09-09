using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Reporting.Jobs;

/// <summary>
/// Story 2: daily recurring job that deletes the stored ZIP for every Completed organisation data
/// export past its 7-day expiry and marks the row Expired. Companies under a legal hold (NFR-07)
/// are skipped so their export data is preserved. A storage-delete failure is logged and does not
/// stop the run. Scheduled from <see cref="ReportingModule.UseReportingRecurringJobs"/>.
///
/// <para>Ticket 3K: this job re-lists and deletes <i>every</i> attempt key for the export (orphans
/// plus the published archive) each run, independent of the artefact-cleanup cursor. So even a
/// Completed export whose orphan archives were already swept has any remaining files removed when it
/// expires here; and if a storage failure leaves work behind, the durable artefact-cleanup sweep
/// still picks the now-Expired row up on a later run via its <c>artefact_cleanup_next_attempt_at</c>
/// cursor.</para>
/// </summary>
internal sealed class PurgeExpiredOrganisationDataExportsJob(
    IOrganisationDataExportJobStore jobStore,
    IOrganisationDataExportStorage storage,
    ILegalHoldStatusReader legalHoldStatusReader,
    ILogger<PurgeExpiredOrganisationDataExportsJob> logger)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var expired = await jobStore.GetExpiredAsync(cancellationToken);

        foreach (var export in expired)
        {
            if (await legalHoldStatusReader.IsUnderLegalHoldAsync(export.CompanyId, cancellationToken))
            {
                logger.LogInformation(
                    "Skipping expiry of organisation data export {ExportId}: company {CompanyId} is under a legal hold.",
                    export.Id, export.CompanyId);
                continue;
            }

            // Follow-up D: remove every attempt archive stored for this export (superseded workers'
            // orphans as well as the published one), not just the published key.
            try
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in await storage.ListAttemptKeysAsync(export.CompanyId, export.Id, cancellationToken))
                    keys.Add(key);
                if (!string.IsNullOrWhiteSpace(export.StorageKey))
                    keys.Add(export.StorageKey!);

                foreach (var key in keys)
                    await storage.DeleteAsync(key, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to delete stored organisation data export {ExportId} ({StorageKey}); marking expired anyway.",
                    export.Id, export.StorageKey);
            }

            await jobStore.MarkExpiredAsync(export.Id, cancellationToken);
        }
    }
}
