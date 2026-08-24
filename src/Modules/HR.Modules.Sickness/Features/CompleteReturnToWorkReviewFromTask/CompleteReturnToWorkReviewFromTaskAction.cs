using HR.Modules.Tasks.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
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
/// If the underlying task is instead completed through the generic
/// POST /tasks/{id}/complete endpoint (bypassing the dedicated review endpoint — e.g. from a
/// generic "My Tasks" list), there is no structured outcome available to record. This handler
/// intentionally leaves the review Pending rather than completing it without a fit-to-return
/// outcome, mirroring LeaveTaskCompletionAction/InterviewFeedbackTaskCompletionAction's
/// established "no decision data supplied => no-op" convention elsewhere in the Tasks
/// integration. The review will still surface as overdue via ReturnToWorkReminderJob /
/// GetOverdueReturnToWorkReviews until it is completed with a real outcome.
/// </summary>
internal sealed class CompleteReturnToWorkReviewFromTaskAction(SicknessDbContext dbContext) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Sickness;
    public TaskActionType ActionType => TaskActionType.Review;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return;

        var review = await dbContext.ReturnToWorkReviews
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Id == context.SourceEntityId.Value && r.CompanyId == context.CompanyId,
                cancellationToken);

        if (review is null || review.Status == ReturnToWorkReviewStatus.Completed)
            return;

        // No structured outcome available on this path — see class remarks. Deliberately not
        // completing the review here.
    }
}
