using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

internal sealed class InterviewFeedbackTaskCompletionAction(IInterviewFeedbackService interviewFeedbackService)
    : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Recruitment;
    public TaskActionType ActionType => TaskActionType.Complete;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null || context.OutcomeDecision is null)
            return;

        await interviewFeedbackService.RecordFeedbackAsync(
            context.CompanyId,
            context.SourceEntityId.Value,
            context.CompletedBy,
            context.OutcomeDecision,
            context.OutcomeReason,
            cancellationToken);
    }
}
