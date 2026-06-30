using HR.SharedKernel;
using HR.SharedKernel.Contracts;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

internal sealed class AssetTaskCompletionAction : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Asset;
    public TaskActionType ActionType => TaskActionType.Acknowledge;

    public Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
