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

        // Any mismatch (missing, wrong company, not completed, expired) is reported as a flat 404 so
        // the endpoint never discloses the existence of another company's export.
        if (export is null
            || export.CompanyId != request.CompanyId
            || export.Status != OrganisationDataExport.StatusCompleted
            || !export.IsDownloadable(now)
            || string.IsNullOrWhiteSpace(export.StorageKey))
        {
            return Result.Failure<DownloadOrganisationDataExportResult>(
                Error.NotFound("No downloadable organisation data export was found."));
        }

        await using var stream = await storage.OpenAsync(export.StorageKey!, cancellationToken);
        if (stream is null)
        {
            return Result.Failure<DownloadOrganisationDataExportResult>(
                Error.NotFound("No downloadable organisation data export was found."));
        }

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        var downloadResult = export.RecordDownload(userId, now);
        if (downloadResult.IsFailure)
            return Result.Failure<DownloadOrganisationDataExportResult>(downloadResult.Error);

        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new OrganisationDataExportDownloadedAuditEvent(
                request.CompanyId, export.Id, userId, export.DownloadCount, now),
            cancellationToken);

        var fileName = $"organisation-data-export-{now:yyyy-MM-dd}.zip";
        return Result.Success(new DownloadOrganisationDataExportResult(bytes, fileName, "application/zip"));
    }
}
