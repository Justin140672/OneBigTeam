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
        var record = await _dbContext.ProbationRecords
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ProbationRecordId,
                cancellationToken);

        if (record is null)
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

        if (review.ReviewType == ProbationReviewType.FinalDecision
            && request.Outcome is not (ProbationOutcome.Pass or ProbationOutcome.Fail or ProbationOutcome.Extend))
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.Validation("A Pass, Fail, or Extend outcome is required when completing a FinalDecision review."));

        if (review.ReviewType == ProbationReviewType.ExtensionConfirmation
            && request.Outcome != ProbationOutcome.Extend)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.Validation("An Extend outcome is required when completing an ExtensionConfirmation review."));

        if (review.ReviewType is not (ProbationReviewType.FinalDecision or ProbationReviewType.ExtensionConfirmation)
            && request.Outcome.HasValue)
            return Result.Failure<CompleteProbationReviewResponse>(
                Error.Validation("Outcome can only be set on FinalDecision or ExtensionConfirmation reviews."));

        var now = _clock.UtcNowOffset();

        if (request.Outcome == ProbationOutcome.Pass)
            record.Pass(request.CompletedByEmployeeId, request.DecisionDate!.Value, request.Notes, now);
        else if (request.Outcome == ProbationOutcome.Fail)
            record.Fail(request.CompletedByEmployeeId, request.DecisionDate!.Value, request.Notes, now);
        else if (request.Outcome == ProbationOutcome.Extend)
            record.Extend(request.NewExpectedEndDate!.Value, request.ExtensionReason!, request.CompletedByEmployeeId, request.DecisionDate!.Value, now);

        review.Complete(request.CompletedByEmployeeId, request.Outcome, request.Notes, now);

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
            review.Outcome?.ToString(),
            review.Notes));
    }
}
