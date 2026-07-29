using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.RemoveReportFavourite;

internal sealed class RemoveReportFavouriteHandler(ReportingDbContext dbContext)
{
    public async Task<Result<RemoveReportFavouriteResponse>> HandleAsync(
        RemoveReportFavouriteRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Idempotent — removing a favourite that doesn't exist is still a success.
        var existing = await dbContext.ReportFavourites
            .Where(f => f.CompanyId == request.CompanyId && f.UserId == userId && f.ReportId == request.ReportId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            dbContext.ReportFavourites.RemoveRange(existing);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new RemoveReportFavouriteResponse(request.ReportId));
    }
}
