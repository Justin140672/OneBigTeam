using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.GetUpcomingProbationReviews;

internal sealed class GetUpcomingProbationReviewsHandler(ProbationDbContext dbContext, IClock clock)
{
    public async Task<Result<GetUpcomingProbationReviewsResponse>> HandleAsync(
        GetUpcomingProbationReviewsRequest request,
        CancellationToken cancellationToken)
    {
        var today   = DateOnly.FromDateTime(clock.UtcNowOffset().DateTime);
        var cutoff  = today.AddDays(30);

        var items = await (
            from review in dbContext.ProbationReviews.AsNoTracking()
            join record in dbContext.ProbationRecords.AsNoTracking()
                on review.ProbationRecordId equals record.Id
            where review.CompanyId == request.CompanyId
               && review.Status    == ProbationReviewStatus.Pending
               && review.DueDate   <= cutoff
            orderby review.DueDate
            select new UpcomingProbationReviewItem(
                review.Id,
                review.ProbationRecordId,
                record.EmployeeId,
                review.ReviewType.ToString(),
                review.DueDate)
        ).ToListAsync(cancellationToken);

        return Result.Success(new GetUpcomingProbationReviewsResponse(items));
    }
}
