using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Features.GetProbationReview;

internal sealed class GetProbationReviewHandler(
    ProbationDbContext dbContext,
    ProbationResourceAuthorizer authorizer)
{
    public async Task<Result<GetProbationReviewResponse>> HandleAsync(
        GetProbationReviewRequest request,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.ProbationReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ReviewId,
                cancellationToken);

        // PROB-02: return NotFound (not Forbidden) for both "doesn't exist" and "exists but
        // caller has no reporting relationship to the employee" — a manager must not be able to
        // distinguish "unrelated review" from "no such review" by guessing review ids.
        if (review is null)
            return Result.Failure<GetProbationReviewResponse>(Error.NotFound("Probation review not found."));

        var record = await dbContext.ProbationRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == review.ProbationRecordId, cancellationToken);

        if (record is null)
            return Result.Failure<GetProbationReviewResponse>(Error.NotFound("Probation record not found."));

        var canView = await authorizer.CanViewEmployeeAsync(
            request.CompanyId, callerEmployeeId, record.EmployeeId, cancellationToken);

        if (!canView)
            return Result.Failure<GetProbationReviewResponse>(Error.NotFound("Probation review not found."));

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
