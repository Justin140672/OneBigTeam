using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.DownloadOrganisationDataExport;

internal sealed class DownloadOrganisationDataExportHandler(
    ReportingDbContext db,
    IOrganisationDataExportStorage storage,
    IAuditEventPublisher auditEventPublisher,
    IClock clock)
{
    public async Task<Result<DownloadOrganisationDataExportResult>> HandleAsync(
        DownloadOrganisationDataExportRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var export = await db.OrganisationDataExports
            .SingleOrDefaultAsync(e => e.Id == request.ExportId, cancellationToken);

        if (export is null
            || export.CompanyId != request.CompanyId
            || export.Status != OrganisationDataExport.StatusCompleted
            || !export.IsDownloadable(now)
            || string.IsNullOrWhiteSpace(export.StorageKey))
        {
            return Result.Failure<DownloadOrganisationDataExportResult>(
                Error.NotFound("No downloadable organisation data export was found."));
        }

        var stream = await storage.OpenAsync(export.StorageKey!, cancellationToken);
        if (stream is null)
        {
            return Result.Failure<DownloadOrganisationDataExportResult>(
                Error.NotFound("No downloadable organisation data export was found."));
        }

        try
        {
            var downloadResult = export.RecordDownload(userId, now);
            if (downloadResult.IsFailure)
            {
                await stream.DisposeAsync();
                return Result.Failure<DownloadOrganisationDataExportResult>(downloadResult.Error);
            }

            await db.SaveChangesAsync(cancellationToken);

            await auditEventPublisher.PublishAsync(
                new OrganisationDataExportDownloadedAuditEvent(
                    request.CompanyId, export.Id, userId, export.DownloadCount, now),
                cancellationToken);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }

        var fileName = $"organisation-data-export-{now:yyyy-MM-dd}.zip";
        long? length = stream.CanSeek ? stream.Length : null;
        return Result.Success(new DownloadOrganisationDataExportResult(stream, length, fileName, "application/zip"));
    }
}
