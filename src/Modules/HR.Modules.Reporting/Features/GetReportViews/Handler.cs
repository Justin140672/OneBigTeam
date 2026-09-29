using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.ReportRegistry;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.GetReportViews;

internal sealed class GetReportViewsHandler(ReportingDbContext dbContext)
{
    public async Task<Result<GetReportViewsResponse>> HandleAsync(
        GetReportViewsRequest request,
        Guid userId,
        ReportAccessGates accessGates,
        CancellationToken cancellationToken)
    {
        if (!ReportCatalog.TryGet(request.ReportId, out var definition) || !accessGates.IsAuthorized(definition.AccessGate))
            return Result.Success(new GetReportViewsResponse([]));

        var views = await dbContext.SavedReportViews
            .AsNoTracking()
            .Where(v => v.CompanyId == request.CompanyId && v.UserId == userId && v.ReportId == request.ReportId)
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.Name)
            .Select(v => new SavedReportViewDto(
                v.Id, v.ReportId, v.Name, v.FilterCriteriaJson, v.IsDefault, v.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new GetReportViewsResponse(views));
    }
}
