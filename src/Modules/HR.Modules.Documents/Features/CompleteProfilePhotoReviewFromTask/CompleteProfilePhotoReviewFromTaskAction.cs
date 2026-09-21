using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CompleteProfilePhotoReviewFromTask;

/// <summary>
/// Wires the generic Tasks "Complete task" endpoint into the real profile-photo review decision
/// (see ITaskCompletionAction). Without this registration, TaskCompletionDispatcher finds no
/// action for (Source: Document, ActionType: Review) and CompleteTaskHandler marks the task
/// Completed with no business effect at all — the review would never actually happen, the pending
/// submission would never resolve, and a caller could complete the task directly via the generic
/// endpoint without ever approving or rejecting anything. This action makes that impossible: it
/// requires an explicit Approve/Reject decision and dispatches to <see cref="ProfilePhotoReviewer"/>
/// — the SAME service that backs the dedicated ApproveProfilePhoto/RejectProfilePhoto endpoints
/// used by the ProfilePhotoReviewPanel UI — so there is exactly one place the actual approve/reject
/// business logic lives, including its own re-validation that the submission is still the
/// employee's current pending one (guards against a stale/superseded submission being approved via
/// a delayed or replayed task-completion call).
///
/// Deliberately depends on ProfilePhotoReviewer directly rather than on
/// ApproveProfilePhotoHandler/RejectProfilePhotoHandler — those handlers depend on ITaskCompleter
/// to close the task themselves (needed for their own dedicated-endpoint callers), and
/// ITaskCompleter's own DI chain resolves through every registered ITaskCompletionAction, including
/// this one; depending on the handlers here would be a circular dependency. Not calling
/// ITaskCompleter here is also correct, not just DI-convenient: CompleteTaskHandler (the caller of
/// this action) completes the TaskItem itself once dispatch succeeds.
/// </summary>
internal sealed class CompleteProfilePhotoReviewFromTaskAction(
    DocumentsDbContext db,
    ProfilePhotoReviewer reviewer) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Document;
    public TaskActionType ActionType => TaskActionType.Review;

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return Result.Failure(Error.Validation("This task has no associated profile photo submission."));

        if (context.OutcomeDecision is not ("Approve" or "Reject"))
            return Result.Failure(Error.Validation(
                "A decision (Approve or Reject) is required to complete a profile photo review."));

        // The task's SourceEntityId is the PendingProfilePhoto.Id captured when the task was
        // created. Re-resolving the employee from it (rather than trusting an employee id supplied
        // separately) both finds who the review is for and — combined with ProfilePhotoReviewer's
        // own re-query by (CompanyId, EmployeeId) — ensures a stale/completed task can never be
        // replayed into approving/rejecting a submission that has since moved on.
        var pendingPhoto = await db.PendingProfilePhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Id == context.SourceEntityId.Value && p.CompanyId == context.CompanyId,
                cancellationToken);

        if (pendingPhoto is null)
        {
            // Ticket 15 (P1) replay-safety: a resumed/retried dispatch for the SAME
            // TaskCompletionOperation (identified by DispatchOperationId) can legitimately find the
            // submission already gone — the first attempt's approve/reject write committed before
            // it was interrupted. That is proof the primary write already applied; treat it as
            // already resolved rather than failing (which would leave the task permanently stuck,
            // since the submission can never reappear). An arbitrary already-resolved call with no
            // dispatch identity (context.DispatchOperationId == Guid.Empty) is NOT given this
            // benefit — it must fail loudly, since it has no evidence a matching attempt ever ran.
            if (context.DispatchOperationId != Guid.Empty)
                return Result.Success();

            return Result.Failure(Error.NotFound(
                "The associated profile photo submission was not found. It may already have been reviewed."));
        }

        // Derives the underlying Approve/Reject request's own idempotency key from the durable
        // TaskCompletionOperation identity (same pattern as LeaveTaskCompletionAction), so a
        // resumed dispatch for the SAME operation replays the original result via
        // ProfilePhotoReviewer's own idempotency support instead of re-applying (or failing on) an
        // already-applied decision.
        var idempotencyKey = context.DispatchOperationId == Guid.Empty
            ? null
            : $"TaskCompletionDispatch:{context.DispatchOperationId}";

        if (context.OutcomeDecision == "Approve")
        {
            var (approveResult, _) = await reviewer.ApproveAsync(
                context.CompanyId, pendingPhoto.EmployeeId, context.CompletedBy, idempotencyKey, cancellationToken);

            return approveResult.IsSuccess ? Result.Success() : Result.Failure(approveResult.Error);
        }

        var (rejectResult, _) = await reviewer.RejectAsync(
            context.CompanyId, pendingPhoto.EmployeeId, context.CompletedBy, context.OutcomeReason,
            idempotencyKey, cancellationToken);

        return rejectResult.IsSuccess ? Result.Success() : Result.Failure(rejectResult.Error);
    }
}
