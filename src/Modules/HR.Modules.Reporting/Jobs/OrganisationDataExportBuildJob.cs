using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Domain;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Reporting.Jobs;

/// <summary>
/// Story 2 / Ticket 3 / Ticket 4: builds the organisation data export ZIP for a single export row,
/// uploads it to dedicated private storage, and marks the export Completed (or Failed). Enqueued
/// one-off by the RequestOrganisationDataExport endpoint and re-enqueued by
/// <see cref="RecoverStalledOrganisationDataExportsJob"/> after an interruption. All cross-module
/// data is obtained through Abstractions contracts — no module-to-module reference.
///
/// Ticket 3 hardening (unchanged): optimistic-concurrency attempt claim, bounded upload retry,
/// fail-whole-export on genuinely missing documents with an administrative alert, and a continuous
/// background ownership-lease renewal loop on its own DbContext scope.
///
/// Ticket 4 (resource limits):
/// <list type="bullet">
///   <item>A process-wide <see cref="IOrganisationDataExportConcurrencyGate"/> caps simultaneous
///   builds. A build that cannot get a slot abandons the run before claiming an attempt and lets
///   Hangfire re-queue it.</item>
///   <item>The archive is assembled straight into a bounded temp-disk workspace
///   (<see cref="IOrganisationDataExportWorkspaceFactory"/>) — never buffered in memory. Module
///   tables are streamed one source at a time; document streams are opened, copied and disposed one
///   at a time.</item>
///   <item>The upload streams from the temp file (no <c>byte[]</c>, no <c>MemoryStream</c> copy).</item>
///   <item>Temp-disk exhaustion fails the export with a clear reason plus an administrative alert.</item>
///   <item>The workspace is deleted on success, failure and cancellation;
///   <see cref="RecoverStalledOrganisationDataExportsJob"/> sweeps workspaces orphaned by a process kill.</item>
/// </list>
/// </summary>
internal sealed class OrganisationDataExportBuildJob(
    IOrganisationDataExportJobStore jobStore,
    IEmployeeDataExportSource employeeSource,
    ILeaveDataExportSource leaveSource,
    ISicknessDataExportSource sicknessSource,
    IRecruitmentDataExportSource recruitmentSource,
    IAuditDataExportSource auditSource,
    IDocumentDataExportManifest documentManifest,
    OrganisationDataExportPackageBuilder packageBuilder,
    IOrganisationDataExportStorage storage,
    IOrganisationDataExportConcurrencyGate concurrencyGate,
    IOrganisationDataExportWorkspaceFactory workspaceFactory,
    IIntegrationEventPublisher integrationEventPublisher,
    IAdministrativeAlertWriter administrativeAlertWriter,
    IOrganisationDataExportLeaseRenewer leaseRenewer,
    IClock clock,
    ILogger<OrganisationDataExportBuildJob> logger)
{
    private const int StorageUploadMaxAttempts = 3;
    private static readonly TimeSpan StorageRetryBackoff = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Follow-up G: how often the background loop renews the ownership lease. Comfortably shorter than
    /// <see cref="OrganisationDataExport.LeaseDurationMinutes"/> so a healthy worker never lets the
    /// lease lapse. Overridable in tests.
    /// </summary>
    internal TimeSpan LeaseRenewInterval { get; init; } =
        TimeSpan.FromMinutes(OrganisationDataExport.LeaseDurationMinutes / 3.0);

    public async Task RunAsync(Guid exportId, Guid companyId, Guid? requestedByUserId, CancellationToken cancellationToken)
    {
        var view = await jobStore.GetAsync(exportId, cancellationToken);
        if (view is null)
        {
            logger.LogInformation("Organisation data export {ExportId} no longer exists; skipping.", exportId);
            return;
        }

        if (view.Status is not (OrganisationDataExport.StatusPending or OrganisationDataExport.StatusInProgress))
        {
            logger.LogInformation(
                "Organisation data export {ExportId} is not runnable (state: {State}); skipping.", exportId, view.Status);
            return;
        }

        // OBT-REM-11: verify the caller-supplied companyId matches the export row being processed.
        if (view.CompanyId != companyId)
        {
            logger.LogError(
                "Organisation data export {ExportId}: company mismatch — job argument {ArgCompanyId} does not match export's company {ActualCompanyId}.",
                exportId, companyId, view.CompanyId);
            throw new InvalidOperationException(
                $"OrganisationDataExport {exportId} does not belong to company {companyId}.");
        }

        // Ticket 4: hold a concurrency slot for the whole build. Acquire it BEFORE claiming an attempt
        // so a queued build never consumes an attempt just by waiting. If no slot frees up, throw so
        // Hangfire re-queues this job with back-off; the recovery sweep is the long-stop.
        await using var slot = await concurrencyGate.AcquireAsync(cancellationToken);
        if (slot is null)
        {
            logger.LogWarning(
                "Organisation data export {ExportId}: no build slot available within the queue-wait window; deferring.", exportId);
            throw new OrganisationDataExportSlotUnavailableException(exportId);
        }

        // Follow-up A: each run takes out its own ownership lease.
        var ownerToken = Guid.NewGuid();

        if (!await jobStore.BeginAttemptAsync(exportId, ownerToken, cancellationToken))
        {
            logger.LogInformation(
                "Organisation data export {ExportId} could not be claimed for an attempt (already owned, terminal, out of attempts, or lost a race); skipping.",
                exportId);
            return;
        }

        using var processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ownershipLost = false;
        var renewalLoop = RenewLeaseContinuouslyAsync();
        var renewalStopped = false;

        async Task StopRenewalAsync()
        {
            if (renewalStopped)
                return;
            renewalStopped = true;
            await renewalCts.CancelAsync();
            try
            {
                await renewalLoop;
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        async Task RenewLeaseContinuouslyAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(LeaseRenewInterval);
                while (await timer.WaitForNextTickAsync(renewalCts.Token))
                {
                    bool stillOwned;
                    try
                    {
                        stillOwned = await leaseRenewer.RenewAsync(exportId, ownerToken, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Organisation data export {ExportId}: lease renewal attempt failed; will retry.", exportId);
                        continue;
                    }

                    if (!stillOwned)
                    {
                        ownershipLost = true;
                        logger.LogWarning(
                            "Organisation data export {ExportId}: ownership lease taken over by another worker; cancelling this attempt.",
                            exportId);
                        await processingCts.CancelAsync();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal: processing finished and the loop was cancelled.
            }
        }

        try
        {
            var cancel = processingCts.Token;

            var fileEntries = await documentManifest.GetFileEntriesAsync(companyId, cancel);

            // Ticket 4: bounded temp-disk workspace; may throw OrganisationDataExportTempCapacityException
            // (free-space margin or combined-budget reservation) before any bytes are written.
            using var workspace = workspaceFactory.CreateWorkspace(exportId);
            await using var archiveStream = workspace.OpenArchiveStream();

            // Ticket 4: every archive byte — including the ZIP central-directory / finalisation write —
            // flows through a counting stream that trips the per-export ceiling mid-write, so a single
            // huge CSV row or document can never blow the budget before the post-entry check runs.
            await using var budgetedArchiveStream = workspace.CreateBudgetEnforcingWriteStream(archiveStream);

            // Ticket 4: stream every module's tables in one source at a time (only one module's rows
            // resident at once), then the documents one stream at a time — straight into the temp file.
            var missing = await packageBuilder.BuildToStreamAsync(
                StreamTablesAsync(companyId, cancel),
                fileEntries,
                (entry, ct) => documentManifest.OpenDocumentAsync(companyId, entry.StorageKey, ct),
                budgetedArchiveStream,
                workspace.EnsureWithinBudget,
                cancel);

            if (missing.Count > 0)
            {
                logger.LogError(
                    "Organisation data export {ExportId} for company {CompanyId}: {MissingCount} expected document(s) missing from storage; failing the export.",
                    exportId, companyId, missing.Count);

                await StopRenewalAsync();
                if (await jobStore.MarkFailedDueToMissingDocumentsAsync(exportId, ownerToken, missing.Count, cancellationToken))
                {
                    await RaiseMissingDocumentsAlertAsync(companyId, exportId, missing, cancellationToken);
                }
                else
                {
                    logger.LogWarning(
                        "Organisation data export {ExportId}: could not record missing-documents failure — no longer the lease owner; not raising an alert.",
                        exportId);
                }
                return;
            }

            cancel.ThrowIfCancellationRequested();

            await archiveStream.FlushAsync(cancel);
            var archiveBytes = archiveStream.Length;

            // Follow-up D: each attempt uploads to its own object key. Ticket 4: streamed from the
            // temp file, never a buffered copy.
            var storageKey = await UploadWithRetryAsync(companyId, exportId, ownerToken, archiveStream, cancel);

            // Ticket 3J: stop the heartbeat before the terminal completion write.
            await StopRenewalAsync();

            if (!await jobStore.MarkCompletedAsync(exportId, ownerToken, storageKey, archiveBytes, cancellationToken))
            {
                logger.LogWarning(
                    "Organisation data export {ExportId}: ownership lease lost before completion; a replacement worker owns it now. Not publishing.",
                    exportId);
                return;
            }

            await CleanUpSiblingAttemptsAsync(companyId, exportId, storageKey, cancellationToken);

            await integrationEventPublisher.PublishAsync(
                new OrganisationDataExportCompletedIntegrationEvent(
                    companyId, exportId, requestedByUserId, clock.UtcNowOffset()),
                cancellationToken);
        }
        catch (OperationCanceledException) when (ownershipLost)
        {
            logger.LogWarning(
                "Organisation data export {ExportId}: ownership lease lost mid-build; abandoning this attempt cleanly (no upload, completion or failure written).",
                exportId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Organisation data export {ExportId}: build cancelled by host shutdown; recovery will re-enqueue it after the lease expires.",
                exportId);
        }
        catch (OrganisationDataExportTempCapacityException ex)
        {
            logger.LogError(ex,
                "Organisation data export {ExportId} for company {CompanyId}: temporary working storage exhausted.",
                exportId, companyId);

            await StopRenewalAsync();
            if (await jobStore.MarkFailedAsync(
                    exportId, ownerToken,
                    "The export could not be completed because temporary working storage was exhausted. Please try again later.",
                    cancellationToken))
            {
                await RaiseTempCapacityAlertAsync(companyId, exportId, ex.Message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Organisation data export {ExportId} for company {CompanyId} failed.", exportId, companyId);

            await StopRenewalAsync();
            if (!await jobStore.MarkFailedAsync(exportId, ownerToken, "Export could not be generated.", cancellationToken))
            {
                logger.LogWarning(
                    "Organisation data export {ExportId}: could not record failure — no longer the lease owner.", exportId);
            }
        }
        finally
        {
            await StopRenewalAsync();
            await processingCts.CancelAsync();
        }
    }

    /// <summary>
    /// Ticket 4: yields every contributing module's tables one source at a time, so only one module's
    /// rows are materialised at any moment (the previous source's list becomes eligible for GC before
    /// the next call).
    /// </summary>
    private async IAsyncEnumerable<DataExportTable> StreamTablesAsync(
        Guid companyId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var source in new Func<Guid, CancellationToken, Task<IReadOnlyList<DataExportTable>>>[]
                 {
                     employeeSource.GetTablesAsync,
                     leaveSource.GetTablesAsync,
                     sicknessSource.GetTablesAsync,
                     recruitmentSource.GetTablesAsync,
                     auditSource.GetTablesAsync,
                     documentManifest.GetTablesAsync,
                 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tables = await source(companyId, cancellationToken);
            foreach (var table in tables)
                yield return table;
        }
    }

    private async Task CleanUpSiblingAttemptsAsync(
        Guid companyId, Guid exportId, string publishedKey, CancellationToken cancellationToken)
    {
        try
        {
            var keys = await storage.ListAttemptKeysAsync(companyId, exportId, cancellationToken);
            foreach (var key in keys)
            {
                if (string.Equals(key, publishedKey, StringComparison.Ordinal))
                    continue;

                await storage.DeleteAsync(key, cancellationToken);
                logger.LogInformation(
                    "Organisation data export {ExportId}: swept up abandoned attempt archive {StorageKey}.", exportId, key);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Organisation data export {ExportId}: failed to sweep up abandoned attempt archives.", exportId);
        }
    }

    private async Task<string> UploadWithRetryAsync(
        Guid companyId, Guid exportId, Guid attemptToken, FileStream archiveStream, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                archiveStream.Position = 0;
                // NonDisposingStreamWrapper: a storage client that disposes its content stream must
                // not close the temp file between retries.
                using var uploadStream = new NonDisposingStreamWrapper(archiveStream);
                return await storage.UploadAsync(companyId, exportId, attemptToken, uploadStream, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < StorageUploadMaxAttempts)
            {
                logger.LogWarning(ex,
                    "Organisation data export {ExportId}: storage upload attempt {Attempt}/{MaxAttempts} failed; retrying.",
                    exportId, attempt, StorageUploadMaxAttempts);
                await Task.Delay(StorageRetryBackoff * attempt, cancellationToken);
            }
        }
    }

    private async Task RaiseMissingDocumentsAlertAsync(
        Guid companyId, Guid exportId, IReadOnlyList<string> missing, CancellationToken cancellationToken)
    {
        try
        {
            var sample = string.Join(", ", missing.Take(5));
            await administrativeAlertWriter.RaiseAsync(new RaiseAdministrativeAlertCommand(
                companyId,
                AdministrativeAlertSeverity.Warning,
                AdministrativeAlertCategory.ReportGeneration,
                "Organisation data export failed: documents missing from storage",
                $"{missing.Count} expected document(s) could not be retrieved from storage while building organisation data export '{exportId}'. Sample: {sample}.",
                clock.UtcNowOffset(),
                DedupKey: $"organisation-data-export-missing-documents:{companyId}",
                AffectedEntityType: "OrganisationDataExport",
                AffectedEntityId: exportId,
                RecommendedAction: "Check the private document storage bucket for the missing objects, then ask the company administrator to request a new export.",
                ActionUrl: null,
                AffectedItemCount: missing.Count,
                Reason: AdministrativeAlertReason.MissingDocumentExport),
                cancellationToken);
        }
        catch (Exception alertEx)
        {
            logger.LogWarning(alertEx,
                "Organisation data export {ExportId}: failed to raise administrative alert for missing documents.", exportId);
        }
    }

    private async Task RaiseTempCapacityAlertAsync(
        Guid companyId, Guid exportId, string detail, CancellationToken cancellationToken)
    {
        // Ticket 4: consistent with the missing-documents alert pattern, but Reason is left null so it
        // does not queue an operations email — capacity clears itself and the admin can re-request.
        try
        {
            await administrativeAlertWriter.RaiseAsync(new RaiseAdministrativeAlertCommand(
                companyId,
                AdministrativeAlertSeverity.Warning,
                AdministrativeAlertCategory.ReportGeneration,
                "Organisation data export failed: temporary working storage exhausted",
                $"Organisation data export '{exportId}' could not be built because temporary working storage was exhausted. {detail}",
                clock.UtcNowOffset(),
                DedupKey: $"organisation-data-export-temp-capacity:{companyId}",
                AffectedEntityType: "OrganisationDataExport",
                AffectedEntityId: exportId,
                RecommendedAction: "Check free disk on the background-job host and the number of concurrent exports, then ask the company administrator to request a new export.",
                ActionUrl: null,
                AffectedItemCount: null,
                Reason: null),
                cancellationToken);
        }
        catch (Exception alertEx)
        {
            logger.LogWarning(alertEx,
                "Organisation data export {ExportId}: failed to raise administrative alert for temp capacity.", exportId);
        }
    }
}
