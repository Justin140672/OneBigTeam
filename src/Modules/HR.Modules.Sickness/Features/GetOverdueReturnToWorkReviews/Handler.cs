using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetOverdueReturnToWorkReviews;

internal sealed class GetOverdueReturnToWorkReviewsHandler(SicknessDbContext dbContext)
{
    public async Task<GetOverdueReturnToWorkReviewsResponse> HandleAsync(
        GetOverdueReturnToWorkReviewsRequest request,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.ReturnToWorkReviews
            .AsNoTracking()
            .Where(r => r.CompanyId == request.CompanyId && r.Status == ReturnToWorkReviewStatus.Overdue)
            .OrderBy(r => r.DueDate)
            .Select(r => new OverdueReturnToWorkReviewItem(
                r.Id,
                r.EmployeeId,
                r.SicknessRecordId,
                r.DueDate))
            .ToListAsync(cancellationToken);

        return new GetOverdueReturnToWorkReviewsResponse(items);
    }
}
