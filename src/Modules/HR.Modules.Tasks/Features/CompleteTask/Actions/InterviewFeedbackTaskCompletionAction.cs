using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

internal sealed class InterviewFeedbackTaskCompletionAction(IInterviewFeedbackService interviewFeedbackService)
    : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Recruitment;
    public TaskActionType ActionType => TaskActionType.Complete;

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return Result.Failure(Error.Validation("This task has no associated interview."));

        if (context.OutcomeDecision is null)
            return Result.Failure(Error.Validation("Feedback outcome is required to complete this task."));

        return await interviewFeedbackService.RecordFeedbackAsync(
            context.CompanyId,
            context.SourceEntityId.Value,
            context.CompletedBy,
            context.OutcomeDecision,
            context.OutcomeReason,
            cancellationToken,
            dispatchOperationId: context.DispatchOperationId);
    }
}
