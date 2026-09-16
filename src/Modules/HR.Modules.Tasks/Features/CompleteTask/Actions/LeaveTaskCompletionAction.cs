using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

internal sealed class LeaveTaskCompletionAction(ILeaveApprovalService leaveApprovalService) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Leave;
    public TaskActionType ActionType => TaskActionType.Approve;

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        // Ticket 3 (P1): a leave-approval task requires an explicit Approve/Reject decision —
        // previously a missing/malformed decision silently no-op'd and the task was still marked
        // Completed by the caller, leaving the leave request forever Pending with no way to tell
        // from the task list that nothing actually happened.
        if (context.SourceEntityId is null)
            return Result.Failure(Error.Validation("This task has no associated leave request."));

        if (context.OutcomeDecision is null)
            return Result.Failure(Error.Validation("A decision (Approve or Reject) is required to complete this task."));

        if (context.OutcomeDecision == "Approve")
        {
            return await leaveApprovalService.ApproveAsync(
                context.CompanyId,
                context.SourceEntityId.Value,
                context.CompletedBy,
                cancellationToken);
        }

        if (context.OutcomeDecision == "Reject")
        {
            return await leaveApprovalService.RejectAsync(
                context.CompanyId,
                context.SourceEntityId.Value,
                context.CompletedBy,
                context.OutcomeReason,
                cancellationToken);
        }

        return Result.Failure(Error.Validation($"'{context.OutcomeDecision}' is not a valid decision for this task."));
    }
}
