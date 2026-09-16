using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

internal sealed class AssetTaskCompletionAction(
    IAssetAcknowledgementService acknowledgementService,
    ITaskCreator taskCreator) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Asset;
    public TaskActionType ActionType => TaskActionType.Acknowledge;

    public async Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return Result.Failure(Error.Validation("This task has no associated asset assignment."));

        // Note: AcknowledgeAsync is already idempotent by domain state (no-ops when
        // AcknowledgedAt is already set — see AssetAcknowledgementService), so it needs no dispatch
        // identity of its own.
        await acknowledgementService.AcknowledgeAsync(
            context.CompanyId,
            context.SourceEntityId.Value,
            context.CompletedBy,
            cancellationToken);

        // Ticket 11 (P1): the "create a return task" effect has no domain-state guard of its own —
        // without a stable idempotency key, a resumed/retried dispatch for the SAME
        // TaskCompletionOperation (interrupted before the task/operation commit) would create a
        // second "Return asset" task. ITaskCreator.CreateAsync already supports this exact pattern
        // (OBT-REM-13) via a database-enforced (company_id, idempotency_key) uniqueness constraint.
        await taskCreator.CreateAsync(
            context.CompanyId,
            createdBy:          context.CompletedBy,
            title:              "Return asset",
            description:        "Please return the assigned asset when it is no longer required.",
            priority:           TaskPriority.Medium,
            source:             TaskSource.Asset,
            actionType:         TaskActionType.Return,
            dueDate:            null,
            assignedEmployeeId: context.AssignedEmployeeId,
            assignedUserId:     null,
            sourceEntityId:     context.SourceEntityId,
            cancellationToken,
            idempotencyKey: context.DispatchOperationId == Guid.Empty
                ? null
                : $"TaskCompletionDispatch:{context.DispatchOperationId}:ReturnAsset");

        return Result.Success();
    }
}
