using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.GetLatestOrganisationDataExport;

internal sealed class GetLatestOrganisationDataExportHandler(ReportingDbContext db, IClock clock)
{
    public async Task<Result<GetLatestOrganisationDataExportResponse>> HandleAsync(
        GetLatestOrganisationDataExportRequest request,
        CancellationToken cancellationToken)
    {
        var latest = await db.OrganisationDataExports
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId)
            .OrderByDescending(e => e.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return Result.Success(new GetLatestOrganisationDataExportResponse(
                null, null, null, null, null, null, false));
        }

        var now = clock.UtcNowOffset();

        return Result.Success(new GetLatestOrganisationDataExportResponse(
            latest.Id,
            latest.Status,
            latest.RequestedAt,
            latest.CompletedAt,
            latest.ExpiresAt,
            latest.FileSizeBytes,
            latest.IsDownloadable(now)));
    }
}
