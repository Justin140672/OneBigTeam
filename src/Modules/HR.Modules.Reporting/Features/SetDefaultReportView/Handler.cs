using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.SetDefaultReportView;

internal sealed class SetDefaultReportViewHandler(ReportingDbContext dbContext)
{
    public async Task<Result<SetDefaultReportViewResponse>> HandleAsync(
        SetDefaultReportViewRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var view = await dbContext.SavedReportViews
            .FirstOrDefaultAsync(
                v => v.Id == request.ViewId && v.CompanyId == request.CompanyId && v.UserId == userId,
                cancellationToken);

        if (view is null)
            return Result.Failure<SetDefaultReportViewResponse>(Error.NotFound("Saved report view not found."));

        var existingDefaults = await dbContext.SavedReportViews
            .Where(v => v.CompanyId == request.CompanyId
                && v.UserId == userId
                && v.ReportId == view.ReportId
                && v.IsDefault
                && v.Id != view.Id)
            .ToListAsync(cancellationToken);

        foreach (var existingDefault in existingDefaults)
            existingDefault.SetIsDefault(false);

        view.SetIsDefault(true);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new SetDefaultReportViewResponse(view.Id, view.IsDefault));
    }
}
