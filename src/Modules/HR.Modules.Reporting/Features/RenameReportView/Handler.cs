using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.RenameReportView;

internal sealed class RenameReportViewHandler(ReportingDbContext dbContext)
{
    public async Task<Result<RenameReportViewResponse>> HandleAsync(
        RenameReportViewRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var view = await dbContext.SavedReportViews
            .FirstOrDefaultAsync(
                v => v.Id == request.ViewId && v.CompanyId == request.CompanyId && v.UserId == userId,
                cancellationToken);

        if (view is null)
            return Result.Failure<RenameReportViewResponse>(Error.NotFound("Saved report view not found."));

        view.Rename(request.Name);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new RenameReportViewResponse(view.Id, view.Name));
    }
}
