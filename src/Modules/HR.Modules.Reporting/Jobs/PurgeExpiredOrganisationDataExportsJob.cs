using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Reporting.Jobs;

/// <summary>
/// Story 2: daily recurring job that deletes the stored ZIP for every Completed organisation data
/// export past its 7-day expiry and marks the row Expired. Companies under a legal hold (NFR-07)
/// are skipped so their export data is preserved. A storage-delete failure is logged and does not
/// stop the run. Scheduled from <see cref="ReportingModule.UseReportingRecurringJobs"/>.
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

            if (!string.IsNullOrWhiteSpace(export.StorageKey))
            {
                try
                {
                    await storage.DeleteAsync(export.StorageKey!, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Failed to delete stored organisation data export {ExportId} ({StorageKey}); marking expired anyway.",
                        export.Id, export.StorageKey);
                }
            }

            await jobStore.MarkExpiredAsync(export.Id, cancellationToken);
        }
    }
}
