using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.CreateProbationReview;

internal sealed class CreateProbationReviewHandler
{
    private readonly ProbationDbContext _dbContext;
    private readonly IClock _clock;

    public CreateProbationReviewHandler(ProbationDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<CreateProbationReviewResponse>> HandleAsync(
        CreateProbationReviewRequest request,
        CancellationToken cancellationToken)
    {
        var recordExists = await _dbContext.ProbationRecords
            .AnyAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ProbationRecordId,
                cancellationToken);

        if (!recordExists)
            return Result.Failure<CreateProbationReviewResponse>(
                Error.NotFound("Probation record not found."));

        var reviewType = Enum.Parse<ProbationReviewType>(request.ReviewType, ignoreCase: true);

        var review = ProbationReview.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.ProbationRecordId,
            reviewType,
            request.DueDate,
            _clock.UtcNowOffset());

        _dbContext.ProbationReviews.Add(review);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateProbationReviewResponse(
            review.Id,
            review.CompanyId,
            review.ProbationRecordId,
            review.ReviewType.ToString(),
            review.DueDate,
            review.Status.ToString(),
            review.CreatedAt));
    }
}
