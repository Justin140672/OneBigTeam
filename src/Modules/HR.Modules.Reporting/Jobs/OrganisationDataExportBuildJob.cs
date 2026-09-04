using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Reporting.Jobs;

/// <summary>
/// Story 2: builds the organisation data export ZIP for a single Pending export row, uploads it to
/// dedicated private storage, and marks the export Completed (or Failed). Enqueued one-off by the
/// RequestOrganisationDataExport endpoint via <see cref="Hangfire.IBackgroundJobClient"/>. All
/// cross-module data is obtained through Abstractions contracts — no module-to-module reference.
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
    IClock clock,
    ILogger<OrganisationDataExportBuildJob> logger)
{
    public async Task RunAsync(Guid exportId, Guid companyId, Guid? requestedByUserId, CancellationToken cancellationToken)
    {
        var view = await jobStore.GetAsync(exportId, cancellationToken);
        if (view is null || view.Status != "Pending")
        {
            logger.LogInformation(
                "Organisation data export {ExportId} is not runnable (state: {State}); skipping.",
                exportId, view?.Status ?? "missing");
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

        try
        {
            await jobStore.MarkInProgressAsync(exportId, cancellationToken);

            var tables = new List<DataExportTable>();
            tables.AddRange(await employeeSource.GetTablesAsync(companyId, cancellationToken));
            tables.AddRange(await leaveSource.GetTablesAsync(companyId, cancellationToken));
            tables.AddRange(await sicknessSource.GetTablesAsync(companyId, cancellationToken));
            tables.AddRange(await recruitmentSource.GetTablesAsync(companyId, cancellationToken));
            tables.AddRange(await auditSource.GetTablesAsync(companyId, cancellationToken));
            tables.AddRange(await documentManifest.GetTablesAsync(companyId, cancellationToken));

            var fileEntries = await documentManifest.GetFileEntriesAsync(companyId, cancellationToken);
            var openedFiles = new List<(string ZipPath, Stream Content)>();
            try
            {
                foreach (var fileEntry in fileEntries)
                {
                    var stream = await documentManifest.OpenDocumentAsync(companyId, fileEntry.StorageKey, cancellationToken);
                    if (stream is not null)
                        openedFiles.Add((fileEntry.ZipPath, stream));
                }

                var zipBytes = packageBuilder.Build(tables, openedFiles);

                using var uploadStream = new MemoryStream(zipBytes, writable: false);
                var storageKey = await storage.UploadAsync(companyId, exportId, uploadStream, cancellationToken);

                await jobStore.MarkCompletedAsync(exportId, storageKey, zipBytes.LongLength, cancellationToken);
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
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Organisation data export {ExportId} for company {CompanyId} failed.", exportId, companyId);
            await jobStore.MarkFailedAsync(exportId, "Export could not be generated.", cancellationToken);
        }
    }
}
