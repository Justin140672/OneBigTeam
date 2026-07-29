using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.DeleteReportView;

internal sealed class DeleteReportViewHandler(ReportingDbContext dbContext)
{
    public async Task<Result<DeleteReportViewResponse>> HandleAsync(
        DeleteReportViewRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var view = await dbContext.SavedReportViews
            .FirstOrDefaultAsync(
                v => v.Id == request.ViewId && v.CompanyId == request.CompanyId && v.UserId == userId,
                cancellationToken);

        if (view is null)
            return Result.Failure<DeleteReportViewResponse>(Error.NotFound("Saved report view not found."));

        dbContext.SavedReportViews.Remove(view);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new DeleteReportViewResponse(view.Id));
    }
}
