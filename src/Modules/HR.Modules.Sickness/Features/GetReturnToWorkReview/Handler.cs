using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetReturnToWorkReview;

internal sealed class GetReturnToWorkReviewHandler(SicknessDbContext dbContext)
{
    public async Task<Result<GetReturnToWorkReviewResponse>> HandleAsync(
        GetReturnToWorkReviewRequest request,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.ReturnToWorkReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ReviewId,
                cancellationToken);

        if (review is null)
            return Result.Failure<GetReturnToWorkReviewResponse>(Error.NotFound("Return-to-work review not found."));

        return Result.Success(new GetReturnToWorkReviewResponse(
            review.Id,
            review.CompanyId,
            review.SicknessRecordId,
            review.EmployeeId,
            review.DueDate,
            review.Status.ToString(),
            review.CompletedAt,
            review.Notes));
    }
}
