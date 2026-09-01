using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.ListOrganisationDataExports;

internal sealed class ListOrganisationDataExportsHandler(ReportingDbContext db, IClock clock)
{
    private const int MaxRows = 50;

    public async Task<Result<ListOrganisationDataExportsResponse>> HandleAsync(
        ListOrganisationDataExportsRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var rows = await db.OrganisationDataExports
            .AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId)
            .OrderByDescending(e => e.RequestedAt)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(e => new OrganisationDataExportListItem(
                e.Id,
                e.Status,
                e.RequestedAt,
                e.CompletedAt,
                e.ExpiresAt,
                e.FileSizeBytes,
                e.DownloadCount,
                e.IsDownloadable(now)))
            .ToList();

        return Result.Success(new ListOrganisationDataExportsResponse(items));
    }
}
