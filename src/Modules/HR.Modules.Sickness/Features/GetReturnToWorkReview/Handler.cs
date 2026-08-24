using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.GetReturnToWorkReview;

internal sealed class GetReturnToWorkReviewHandler(
    SicknessDbContext dbContext,
    SicknessResourceAuthorizer authorizer)
{
    public async Task<Result<GetReturnToWorkReviewResponse>> HandleAsync(
        GetReturnToWorkReviewRequest request,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.ReturnToWorkReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ReviewId,
                cancellationToken);

        // SICK-02: return NotFound (not Forbidden) for both "doesn't exist" and "exists but
        // caller has no reporting relationship to the employee" — a manager must not be able to
        // distinguish "unrelated review" from "no such review" by guessing review ids.
        if (review is null)
            return Result.Failure<GetReturnToWorkReviewResponse>(Error.NotFound("Return-to-work review not found."));

        var isHrAdministrator = await authorizer.IsHrAdministratorAsync(callerEmployeeId, cancellationToken);

        if (!isHrAdministrator)
        {
            var canView = await authorizer.CanViewEmployeeAsync(
                request.CompanyId, callerEmployeeId, review.EmployeeId, cancellationToken);

            if (!canView)
                return Result.Failure<GetReturnToWorkReviewResponse>(Error.NotFound("Return-to-work review not found."));
        }

        // Review notes may contain sensitive medical/return-to-work detail. HR Administrators
        // see the full record; managers get a trimmed view that omits Notes, per SICK-02 ("avoid
        // returning category, notes or evidence information where a limited manager view does
        // not require it").
        //
        // SICK-03: AdjustmentsRequired/AdjustmentDetails are NOT trimmed for managers — unlike
        // free-text Notes, adjustment details describe what the manager themselves needs to put
        // in place for the employee's return (e.g. phased hours, altered duties), so a manager
        // acting on the review needs to see them regardless of HR Administrator status. Outcome
        // is likewise visible to both — it's the headline decision the review exists to record.
        return Result.Success(new GetReturnToWorkReviewResponse(
            review.Id,
            review.CompanyId,
            review.SicknessRecordId,
            review.EmployeeId,
            review.DueDate,
            review.Status.ToString(),
            review.CompletedAt,
            isHrAdministrator ? review.Notes : null,
            review.Outcome?.ToString(),
            review.AdjustmentsRequired,
            review.AdjustmentDetails));
    }
}
