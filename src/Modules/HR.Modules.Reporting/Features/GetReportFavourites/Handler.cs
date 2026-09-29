using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.GetReportFavourites;

internal sealed class GetReportFavouritesHandler(ReportingDbContext dbContext)
{
    public async Task<Result<GetReportFavouritesResponse>> HandleAsync(
        GetReportFavouritesRequest request,
        Guid userId,
        ReportAccessGates accessGates,
        CancellationToken cancellationToken)
    {
        var reportIds = await dbContext.ReportFavourites
            .AsNoTracking()
            .Where(f => f.CompanyId == request.CompanyId && f.UserId == userId)
            .Select(f => f.ReportId)
            .ToListAsync(cancellationToken);

        var visibleReportIds = reportIds
            .Where(reportId => ReportCatalog.TryGet(reportId, out var definition) && accessGates.IsAuthorized(definition.AccessGate))
            .ToList();

        return Result.Success(new GetReportFavouritesResponse(visibleReportIds));
    }
}
