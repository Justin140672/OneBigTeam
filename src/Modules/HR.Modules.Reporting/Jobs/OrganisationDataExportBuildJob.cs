using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Domain;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Reporting.Jobs;

/// <summary>
/// Story 2 / Ticket 3: builds the organisation data export ZIP for a single export row, uploads it to
/// dedicated private storage, and marks the export Completed (or Failed). Enqueued one-off by the
/// RequestOrganisationDataExport endpoint and re-enqueued by
/// <see cref="RecoverStalledOrganisationDataExportsJob"/> after an interruption. All cross-module
/// data is obtained through Abstractions contracts — no module-to-module reference.
///
/// Ticket 3 hardening:
/// <list type="bullet">
///   <item>The Pending/InProgress -&gt; InProgress claim goes through
///   <see cref="IOrganisationDataExportJobStore.BeginAttemptAsync"/> which is optimistic-concurrency
///   guarded, so two workers racing the same row can never both build it.</item>
///   <item>Transient storage upload failures are retried a bounded number of times before the
///   attempt fails.</item>
///   <item>If an expected document is genuinely missing from storage the export is <b>failed</b>
///   (never completed with a partial archive) and an administrative alert is raised. The Documents
///   records are left untouched.</item>
/// </list>
///
/// Follow-up G: the ownership lease is renewed continuously by a background loop
/// (<see cref="LeaseRenewInterval"/>) that runs on its <b>own</b> DbContext scope for the entire
/// build and upload — not just at checkpoints. If the loop finds the lease has been taken over it
/// cancels processing through a linked <see cref="CancellationTokenSource"/> so the attempt unwinds
/// without uploading, completing or failing. The atomic ownership check in
/// <see cref="IOrganisationDataExportJobStore.MarkCompletedAsync"/> remains the final publish guard.
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

        // OBT-REM-11: verify the caller-supplied companyId (used by the Hangfire failure-audit
        // filter to scope this job to a tenant) actually matches the export row being processed.
        if (view.CompanyId != companyId)
        {
            logger.LogError(
                "Organisation data export {ExportId}: company mismatch — job argument {ArgCompanyId} does not match export's company {ActualCompanyId}.",
                exportId, companyId, view.CompanyId);
            throw new InvalidOperationException(
                $"OrganisationDataExport {exportId} does not belong to company {companyId}.");
        }

        // Follow-up A: each run takes out its own ownership lease. Only the holder of this token may
        // renew the lease, complete the export or fail it for missing documents — a superseded worker
        // is locked out and cannot overwrite a published archive.
        var ownerToken = Guid.NewGuid();

        if (!await jobStore.BeginAttemptAsync(exportId, ownerToken, cancellationToken))
        {
            logger.LogInformation(
                "Organisation data export {ExportId} could not be claimed for an attempt (already owned by a live lease, terminal, out of attempts, or lost a race); skipping.",
                exportId);
            return;
        }

        // Follow-up G: cancel the processing pipeline the moment ownership is lost.
        using var processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Follow-up G: stops the renewal loop itself as soon as processing finishes/fails.
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ownershipLost = false;
        var renewalLoop = RenewLeaseContinuouslyAsync();
        var renewalStopped = false;

        // Ticket 3J: stop and drain the heartbeat loop *before* any terminal write so a renewal can
        // never interleave with completion/failure (bump the version and force a benign retry, or
        // land after the terminal transition). The store's ownership guard remains the hard barrier
        // for a superseded worker.
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
                        // Follow-up G: renewal runs on its own DbContext scope (see the renewer),
                        // never the worker's ReportingDbContext.
                        stillOwned = await leaseRenewer.RenewAsync(exportId, ownerToken, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Transient renewal failure: try again next tick. If it keeps failing until the
                        // lease actually lapses, recovery will legitimately take over and the next
                        // renewal returns false.
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

            var tables = new List<DataExportTable>();
            tables.AddRange(await employeeSource.GetTablesAsync(companyId, cancel));
            tables.AddRange(await leaveSource.GetTablesAsync(companyId, cancel));
            tables.AddRange(await sicknessSource.GetTablesAsync(companyId, cancel));
            tables.AddRange(await recruitmentSource.GetTablesAsync(companyId, cancel));
            tables.AddRange(await auditSource.GetTablesAsync(companyId, cancel));
            tables.AddRange(await documentManifest.GetTablesAsync(companyId, cancel));

            var fileEntries = await documentManifest.GetFileEntriesAsync(companyId, cancel);
            var openedFiles = new List<(string ZipPath, Stream Content)>();
            var missing = new List<string>();
            try
            {
                foreach (var fileEntry in fileEntries)
                {
                    cancel.ThrowIfCancellationRequested();
                    var stream = await documentManifest.OpenDocumentAsync(companyId, fileEntry.StorageKey, cancel);
                    if (stream is null)
                        missing.Add(fileEntry.ZipPath);
                    else
                        openedFiles.Add((fileEntry.ZipPath, stream));
                }

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
                var zipBytes = packageBuilder.Build(tables, openedFiles);

                // Follow-up A/G: if a replacement worker has already taken ownership, abandon now —
                // never upload over a published archive.
                cancel.ThrowIfCancellationRequested();

                // Follow-up D: each attempt uploads to its own object key (keyed by the owner token) so
                // concurrent or retried attempts never overwrite one another.
                var storageKey = await UploadWithRetryAsync(companyId, exportId, ownerToken, zipBytes, cancel);

                // Ticket 3J: stop the heartbeat before the terminal completion write.
                await StopRenewalAsync();

                // Follow-up A/D: publish the winning archive path only via the atomic, ownership-guarded
                // completion. A superseded worker gets false here and abandons its (now orphan) upload.
                if (!await jobStore.MarkCompletedAsync(exportId, ownerToken, storageKey, zipBytes.LongLength, cancellationToken))
                {
                    logger.LogWarning(
                        "Organisation data export {ExportId}: ownership lease lost before completion; a replacement worker owns it now. Not publishing.",
                        exportId);
                    return;
                }

                // Follow-up D: winner sweeps up any sibling attempt archives left by superseded workers.
                await CleanUpSiblingAttemptsAsync(companyId, exportId, storageKey, cancellationToken);
            }
            finally
            {
                foreach (var (_, content) in openedFiles)
                    await content.DisposeAsync();
            }

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
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Organisation data export {ExportId} for company {CompanyId} failed.", exportId, companyId);

            // Ticket 3J: stop the heartbeat before the terminal failure write.
            await StopRenewalAsync();

            // Follow-up D: only the still-current owner may record the transient failure.
            if (!await jobStore.MarkFailedAsync(exportId, ownerToken, "Export could not be generated.", cancellationToken))
            {
                logger.LogWarning(
                    "Organisation data export {ExportId}: could not record failure — no longer the lease owner.", exportId);
            }
        }
        finally
        {
            // Follow-up G / Ticket 3J: stop renewal immediately when the attempt finishes, fails or
            // loses ownership. Idempotent — the terminal-write paths above already drained it.
            await StopRenewalAsync();
            await processingCts.CancelAsync();
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

    private async Task<string> UploadWithRetryAsync(Guid companyId, Guid exportId, Guid attemptToken, byte[] zipBytes, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var uploadStream = new MemoryStream(zipBytes, writable: false);
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
        // Best-effort: an alert-writer failure must never mask the export failure itself.
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
}
