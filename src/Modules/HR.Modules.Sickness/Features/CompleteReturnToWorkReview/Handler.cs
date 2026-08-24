using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.CompleteReturnToWorkReview;

/// <summary>
/// The canonical way a return-to-work review is completed (SICK-03). Validates and records the
/// structured fit-to-return outcome on the review itself, applies the "not fit to return"
/// reopen decision to the parent SicknessRecord, then calls ITaskCompleter so the underlying
/// task workflow also reflects completion — mirroring
/// HR.Modules.Documents.Features.CompleteSharedCompanyDocumentReview's established pattern of a
/// module owning full validation for its own "complete" action while still routing through the
/// shared Tasks completion machinery.
/// </summary>
internal sealed class CompleteReturnToWorkReviewHandler(
    SicknessDbContext db,
    SicknessResourceAuthorizer authorizer,
    ITaskCompleter taskCompleter,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<CompleteReturnToWorkReviewResponse>> HandleAsync(
        CompleteReturnToWorkReviewRequest request,
        Guid reviewedBy,
        CancellationToken cancellationToken)
    {
        var review = await db.ReturnToWorkReviews
            .FirstOrDefaultAsync(
                r => r.CompanyId == request.CompanyId && r.Id == request.ReviewId,
                cancellationToken);

        // Mirrors GetReturnToWorkReviewHandler: NotFound (not Forbidden) for both "doesn't
        // exist" and "caller has no reporting relationship to the employee", so a manager can't
        // distinguish "unrelated review" from "no such review" by guessing review ids.
        if (review is null)
            return Result.Failure<CompleteReturnToWorkReviewResponse>(Error.NotFound("Return-to-work review not found."));

        var isHrAdministrator = await authorizer.IsHrAdministratorAsync(reviewedBy, cancellationToken);

        if (!isHrAdministrator)
        {
            var canView = await authorizer.CanViewEmployeeAsync(
                request.CompanyId, reviewedBy, review.EmployeeId, cancellationToken);

            if (!canView)
                return Result.Failure<CompleteReturnToWorkReviewResponse>(Error.NotFound("Return-to-work review not found."));
        }

        // Idempotency (AC: "repeated task completion does not overwrite or duplicate the
        // review"): a second completion call — e.g. a retried request, or the Tasks module
        // re-dispatching after ITaskCompleter below — returns the already-recorded outcome
        // as-is rather than re-validating, re-mutating, or re-publishing audit events.
        var wasAlreadyCompleted = review.Status == ReturnToWorkReviewStatus.Completed;

        var now = clock.UtcNowOffset();

        if (!wasAlreadyCompleted)
        {
            review.Complete(
                reviewedBy,
                request.Outcome,
                request.AdjustmentsRequired,
                request.AdjustmentDetails,
                request.ManagerNotes,
                now);

            if (request.Outcome == FitToReturnOutcome.NotFit)
            {
                var sicknessRecord = await db.SicknessRecords
                    .FirstOrDefaultAsync(
                        s => s.Id == review.SicknessRecordId && s.CompanyId == request.CompanyId,
                        cancellationToken);

                // Defensive: the sickness record is expected to always exist (FK-enforced), but
                // a missing record should not block recording the review outcome itself.
                if (sicknessRecord is not null)
                {
                    sicknessRecord.ReopenFollowingUnfitReview(now);

                    await auditPublisher.PublishAsync(new SicknessRecordReopenedAuditEvent(
                        sicknessRecord.CompanyId,
                        sicknessRecord.EmployeeId,
                        sicknessRecord.Id,
                        review.Id,
                        reviewedBy,
                        now), cancellationToken);
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            await auditPublisher.PublishAsync(new ReturnToWorkReviewCompletedAuditEvent(
                review.Id,
                review.SicknessRecordId,
                review.CompanyId,
                review.EmployeeId,
                reviewedBy,
                review.Outcome!.Value.ToString(),
                review.AdjustmentsRequired,
                HasAdjustmentDetails: !string.IsNullOrWhiteSpace(review.AdjustmentDetails),
                HasNotes: !string.IsNullOrWhiteSpace(review.Notes),
                now,
                now), cancellationToken);

            // Closes the task created when the review was raised (CloseSicknessRecordHandler ->
            // ReturnToWorkReviewRequiredIntegrationEvent -> Tasks module), so the review no
            // longer shows as an open task once its outcome is recorded. This re-enters
            // CompleteReturnToWorkReviewFromTaskAction via the dispatcher, but the review is
            // already Completed by this point, so that re-entry is a safe no-op. No-op here too
            // if no matching open task exists.
            await taskCompleter.CompleteBySourceEntityAsync(
                review.CompanyId,
                review.Id,
                TaskSource.Sickness,
                TaskActionType.Review,
                reviewedBy,
                cancellationToken);
        }

        return Result.Success(new CompleteReturnToWorkReviewResponse(
            review.Id,
            review.CompanyId,
            review.SicknessRecordId,
            review.EmployeeId,
            review.Status.ToString(),
            review.Outcome!.Value.ToString(),
            review.AdjustmentsRequired,
            review.AdjustmentDetails,
            review.ReviewedBy!.Value,
            review.CompletedAt!.Value));
    }
}
