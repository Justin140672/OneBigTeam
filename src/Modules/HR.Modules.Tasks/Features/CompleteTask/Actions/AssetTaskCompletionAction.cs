using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

internal sealed class AssetTaskCompletionAction(
    IAssetAcknowledgementService acknowledgementService,
    ITaskCreator taskCreator,
    IClock clock) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Asset;
    public TaskActionType ActionType => TaskActionType.Acknowledge;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return;

        await acknowledgementService.AcknowledgeAsync(
            context.CompanyId,
            context.SourceEntityId.Value,
            context.CompletedBy,
            cancellationToken);

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
            cancellationToken);
    }
}
