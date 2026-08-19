using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

internal sealed class LeaveTaskCompletionAction(ILeaveApprovalService leaveApprovalService) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Leave;
    public TaskActionType ActionType => TaskActionType.Approve;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null || context.OutcomeDecision is null)
            return;

        if (context.OutcomeDecision == "Approve")
        {
            await leaveApprovalService.ApproveAsync(
                context.CompanyId,
                context.SourceEntityId.Value,
                context.CompletedBy,
                cancellationToken);
        }
        else if (context.OutcomeDecision == "Reject")
        {
            await leaveApprovalService.RejectAsync(
                context.CompanyId,
                context.SourceEntityId.Value,
                context.CompletedBy,
                context.OutcomeReason,
                cancellationToken);
        }
    }
}
