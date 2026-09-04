using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Hangfire;

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
        await db.SaveChangesAsync(cancellationToken);

        backgroundJobClient.Enqueue<OrganisationDataExportBuildJob>(
            job => job.RunAsync(export.Id, request.CompanyId, userId, CancellationToken.None));

        await auditEventPublisher.PublishAsync(
            new OrganisationDataExportRequestedAuditEvent(request.CompanyId, export.Id, userId, now),
            cancellationToken);

        return Result.Success(new RequestOrganisationDataExportResponse(export.Id, export.Status));
    }
}
