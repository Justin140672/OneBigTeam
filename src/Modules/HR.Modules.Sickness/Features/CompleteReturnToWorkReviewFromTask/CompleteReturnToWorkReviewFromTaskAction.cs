using HR.Modules.Tasks.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Features.CompleteReturnToWorkReviewFromTask;

/// <summary>
/// Reacts when the Tasks module marks a return-to-work review's underlying task as Completed.
///
/// SICK-03: the review's structured fit-to-return outcome (Fit/FitWithAdjustments/NotFit,
/// whether adjustments are required, adjustment details, manager notes) can only be captured
/// through the dedicated Features/CompleteReturnToWorkReview endpoint — that handler validates
/// the outcome, completes the review itself, decides whether to reopen the sickness record, and
/// only then calls ITaskCompleter to close the underlying task. That call re-enters here via the
/// Tasks module's completion dispatcher, but by that point the review is already Completed, so
/// the guard below makes this a safe, audit-free no-op (idempotency: "repeated task completion
/// does not overwrite or duplicate the review").
///
/// Ticket 16 (P1): if the underlying task is instead completed through the generic
/// POST /tasks/{id}/complete endpoint (bypassing the dedicated review endpoint — e.g. from a
/// generic "My Tasks" list), there is no structured outcome available to record. Previously this
/// returned <see cref="Result.Success()"/> for that case too — CompleteTaskHandler then marked the
/// generic Tasks item Completed even though the review itself remained Pending, contradicting the
/// rule (see CompleteReturnToWorkReviewHandler) that a review can never resolve without a valid
/// fit-to-return decision. This now returns a validation failure instead: CompleteTaskHandler
/// aborts completion entirely on a failed Result (see ITaskCompletionAction's doc), so the generic
/// task stays open/actionable and the caller is told to use the dedicated review flow. The
/// dedicated endpoint's own call back into this dispatcher still hits the "already Completed"
/// branch above and succeeds, completing the linked task exactly once.
/// </summary>
internal sealed class CompleteReturnToWorkReviewFromTaskAction(SicknessDbContext dbContext) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Sickness;
    public TaskActionType ActionType => TaskActionType.Review;

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return Result.Success();

        var review = await dbContext.ReturnToWorkReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Id == context.SourceEntityId.Value && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (review is null)
            return Result.Success();

        if (review.Status == ReturnToWorkReviewStatus.Completed)
            return Result.Success();

        // Ticket 16 (P1): no structured fit-to-return outcome is available on this generic path —
        // reject rather than silently completing the generic task while the review stays Pending.
        return Result.Failure(Error.Validation(
            "A return-to-work review can only be completed with a fit-to-return decision. " +
            "Use the return-to-work review screen to record the outcome."));
    }
}
