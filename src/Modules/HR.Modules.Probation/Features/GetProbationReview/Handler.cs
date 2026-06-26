using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.GetProbationReview;

internal sealed class GetProbationReviewHandler(ProbationDbContext dbContext)
{
    public async Task<Result<GetProbationReviewResponse>> HandleAsync(
        GetProbationReviewRequest request,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.ProbationReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ReviewId,
                cancellationToken);

        if (review is null)
            return Result.Failure<GetProbationReviewResponse>(Error.NotFound("Probation review not found."));

        var record = await dbContext.ProbationRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == review.ProbationRecordId, cancellationToken);

        if (record is null)
            return Result.Failure<GetProbationReviewResponse>(Error.NotFound("Probation record not found."));

        return Result.Success(new GetProbationReviewResponse(
            review.Id,
            review.CompanyId,
            review.ProbationRecordId,
            record.EmployeeId,
            review.ReviewType.ToString(),
            review.DueDate,
            review.Status.ToString(),
            review.CompletedAt,
            review.Notes,
            record.StartDate,
            record.ExpectedEndDate,
            record.Status.ToString()));
    }
}
