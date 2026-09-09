using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.RequestOrganisationDataExport;

internal sealed class RequestOrganisationDataExportHandler(
    ReportingDbContext db,
    IOrganisationDataExportStatusReader statusReader,
    IBackgroundJobClient backgroundJobClient,
    IAuditEventPublisher auditEventPublisher,
    IClock clock)
{
    public async Task<Result<RequestOrganisationDataExportResponse>> HandleAsync(
        RequestOrganisationDataExportRequest request,
        Guid userId,
        string? requestedByDisplayName,
        CancellationToken cancellationToken)
    {
        if (await statusReader.HasActiveExportAsync(request.CompanyId, cancellationToken))
        {
            return Result.Failure<RequestOrganisationDataExportResponse>(Error.Conflict(
                "An organisation data export is already being prepared for this company. Wait for it to finish before requesting another."));
        }

        var now = clock.UtcNowOffset();
        var export = OrganisationDataExport.Create(request.CompanyId, userId, requestedByDisplayName, now);

        db.OrganisationDataExports.Add(export);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Ticket 3: lost a race with a concurrent request/retry on the
            // ix_organisation_data_exports_active_per_company filtered unique index.
            return Result.Failure<RequestOrganisationDataExportResponse>(Error.Conflict(
                "An organisation data export is already being prepared for this company. Wait for it to finish before requesting another."));
        }

        backgroundJobClient.Enqueue<OrganisationDataExportBuildJob>(
            job => job.RunAsync(export.Id, request.CompanyId, userId, CancellationToken.None));

        await auditEventPublisher.PublishAsync(
            new OrganisationDataExportRequestedAuditEvent(request.CompanyId, export.Id, userId, now),
            cancellationToken);

        return Result.Success(new RequestOrganisationDataExportResponse(export.Id, export.Status));
    }
}
