using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

internal sealed class AssetReturnTaskCompletionAction(IAssetReturnService assetReturnService) : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Asset;
    public TaskActionType ActionType => TaskActionType.Return;

    public async Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        if (context.SourceEntityId is null)
            return;

        await assetReturnService.ReturnAsync(
            context.CompanyId,
            context.SourceEntityId.Value,
            context.CompletedBy,
            cancellationToken);
    }
}
