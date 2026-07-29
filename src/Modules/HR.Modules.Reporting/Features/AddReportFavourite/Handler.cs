using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Features.AddReportFavourite;

internal sealed class AddReportFavouriteHandler(ReportingDbContext dbContext, IClock clock)
{
    public async Task<Result<AddReportFavouriteResponse>> HandleAsync(
        AddReportFavouriteRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Idempotent — favouriting an already-favourited report is a no-op success, not a conflict.
        var exists = await dbContext.ReportFavourites
            .AsNoTracking()
            .AnyAsync(f => f.CompanyId == request.CompanyId && f.UserId == userId && f.ReportId == request.ReportId,
                cancellationToken);

        if (!exists)
        {
            dbContext.ReportFavourites.Add(ReportFavourite.Create(
                Guid.NewGuid(), request.CompanyId, userId, request.ReportId, clock.UtcNowOffset()));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new AddReportFavouriteResponse(request.ReportId));
    }
}
