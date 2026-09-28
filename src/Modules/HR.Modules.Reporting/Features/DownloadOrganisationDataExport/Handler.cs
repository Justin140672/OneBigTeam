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
    IClock clock,
    IAuthorizationService authorizationService)
{
    public async Task<Result<DownloadOrganisationDataExportResult>> HandleAsync(
        DownloadOrganisationDataExportRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Ticket 5: Organisation data exports require BOTH Company Administrator AND HR Administrator roles.
        // Company Administrator alone (without HR Admin role) is insufficient.
        var effectiveRoles = await authorizationService.GetEffectiveRolesAsync(userId, cancellationToken);

        // SystemRoles.HrAdministrator = 00000000-0000-0000-0000-000000000004
        // SystemRoles.CompanyAdministrator = 00000000-0000-0000-0000-000000000006
        var hrAdministratorRoleId = new Guid("00000000-0000-0000-0000-000000000004");
        var companyAdministratorRoleId = new Guid("00000000-0000-0000-0000-000000000006");

        var hasHrAdminRole = effectiveRoles.Contains(hrAdministratorRoleId);
        var hasCompanyAdminRole = effectiveRoles.Contains(companyAdministratorRoleId);

        if (!hasHrAdminRole || !hasCompanyAdminRole)
        {
            return Result.Failure<DownloadOrganisationDataExportResult>(
                Error.Forbidden("This action requires both Company Administrator and HR Administrator roles."));
        }

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
