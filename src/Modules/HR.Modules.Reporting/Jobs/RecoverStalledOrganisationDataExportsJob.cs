using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Domain;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Reporting.Jobs;

/// <summary>
/// Ticket 3: recurring sweep that recovers organisation data exports left in an unfinished state by a
/// process interruption:
/// <list type="bullet">
///   <item><b>Saved but never queued.</b> A Pending row older than <see cref="PendingQueueGraceMinutes"/>
///   (the request committed but the process died before <c>IBackgroundJobClient.Enqueue</c>) is
///   (re-)enqueued. Re-enqueuing a row whose job is actually still queued is harmless — the build
///   job's optimistic-concurrency claim means only one attempt ever runs.</item>
///   <item><b>Interrupted mid-build.</b> An InProgress row whose worker ownership lease has expired
///   (the worker crashed and stopped heartbeating) is reset and re-enqueued while it still has
///   attempts left, or failed once it has exhausted <see cref="OrganisationDataExport.MaxAttempts"/>.
///   A healthy long-running export keeps renewing its lease and is never reclaimed.</item>
/// </list>
/// Idempotent and safe to run repeatedly. Scheduled from
/// <see cref="ReportingModule.UseReportingRecurringJobs"/>.
/// </summary>
[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 600)]
internal sealed class RecoverStalledOrganisationDataExportsJob(
    IOrganisationDataExportJobStore jobStore,
    IBackgroundJobClient backgroundJobClient,
    IOrganisationDataExportStorage storage,
    IClock clock,
    ILogger<RecoverStalledOrganisationDataExportsJob> logger)
{
    public const int PendingQueueGraceMinutes = 5;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNowOffset();

        var recoverable = await jobStore.GetRecoverableAsync(
            now.AddMinutes(-PendingQueueGraceMinutes),
            now,
            cancellationToken);

        foreach (var export in recoverable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (export.Status == OrganisationDataExport.StatusPending)
            {
                Enqueue(export);
                logger.LogWarning(
                    "Recovered organisation data export {ExportId}: was saved but never queued; re-enqueued build job.",
                    export.Id);
                continue;
            }

            // Follow-up H: InProgress and apparently stale. Atomically claim recovery ownership before
            // touching any files. The claim rechecks status + lease expiry, so it is abandoned if the
            // original worker has completed, renewed its lease, or another recovery sweep won the race.
            var recoveryToken = Guid.NewGuid();
            if (!await jobStore.ClaimForRecoveryAsync(export.Id, recoveryToken, cancellationToken))
            {
                logger.LogInformation(
                    "Recovery abandoned for organisation data export {ExportId}: it completed, renewed its lease, or changed ownership since the recovery scan.",
                    export.Id);
                continue;
            }

            // We now hold the lease. Any attempt archives belong to dead/superseded workers and are
            // safe to sweep — but never the published archive (guarded in CleanUpAttemptFilesAsync).
            await CleanUpAttemptFilesAsync(export, cancellationToken);

            if (export.AttemptCount < OrganisationDataExport.MaxAttempts)
            {
                if (await jobStore.ResetForRetryAsync(export.Id, recoveryToken, cancellationToken))
                {
                    Enqueue(export);
                    logger.LogWarning(
                        "Recovered organisation data export {ExportId}: interrupted mid-build after {Attempts} attempt(s); reset and re-enqueued.",
                        export.Id, export.AttemptCount);
                }
                else
                {
                    logger.LogInformation(
                        "Recovery reset skipped for organisation data export {ExportId}: ownership changed after the recovery claim.",
                        export.Id);
                }
            }
            else
            {
                await jobStore.MarkFailedAsync(
                    export.Id,
                    recoveryToken,
                    "Export was interrupted and did not complete after repeated attempts.",
                    cancellationToken);
                logger.LogError(
                    "Failed organisation data export {ExportId}: interrupted and exceeded the retry limit ({MaxAttempts} attempts).",
                    export.Id, OrganisationDataExport.MaxAttempts);
            }
        }
    }

    private async Task CleanUpAttemptFilesAsync(OrganisationDataExportJobView export, CancellationToken cancellationToken)
    {
        try
        {
            var keys = await storage.ListAttemptKeysAsync(export.CompanyId, export.Id, cancellationToken);
            foreach (var key in keys)
            {
                // Follow-up H: never delete a published archive during recovery cleanup.
                if (!string.IsNullOrWhiteSpace(export.StorageKey) &&
                    string.Equals(key, export.StorageKey, StringComparison.Ordinal))
                    continue;

                await storage.DeleteAsync(key, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Organisation data export {ExportId}: failed to sweep abandoned attempt archives during recovery.",
                export.Id);
        }
    }

    private void Enqueue(OrganisationDataExportJobView export) =>
        backgroundJobClient.Enqueue<OrganisationDataExportBuildJob>(
            job => job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None));
}
