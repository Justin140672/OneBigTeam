using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CompleteProbationReview;

internal sealed class CompleteProbationReviewHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;

    public CompleteProbationReviewHandler(ProbationDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CompleteProbationReviewResponse>> HandleAsync(
        CompleteProbationReviewRequest request,
        CancellationToken cancellationToken)
    {
        var recordExists = await _dbContext.ProbationRecords
            .AnyAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ProbationRecordId,
                cancellationToken);

        if (!recordExists)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.NotFound("Probation record not found."));

        var review = await _dbContext.ProbationReviews
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId
                     && r.ProbationRecordId == request.ProbationRecordId
                     && r.Id == request.ReviewId,
                cancellationToken);

        if (review is null)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.NotFound("Probation review not found."));

        if (review.Status == ProbationReviewStatus.Completed)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.Validation("Probation review is already completed."));

        review.Complete(request.CompletedByEmployeeId, request.Notes, _clock.UtcNowOffset());

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CompleteProbationReviewResponse(
            review.Id,
            review.CompanyId,
            review.ProbationRecordId,
            review.ReviewType.ToString(),
            review.DueDate,
            review.Status.ToString(),
            review.CompletedAt,
            review.CompletedByEmployeeId,
            review.Notes));
    }
}
