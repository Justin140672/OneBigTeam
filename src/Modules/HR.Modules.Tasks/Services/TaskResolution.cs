using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskResolution(TaskCompleter completer, TaskCanceller canceller) : ITaskResolution
{
    public Task<TaskResolutionResult> CompleteBySourceEntityConfirmedAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid completedBy,
        CancellationToken cancellationToken,
        TaskCompletionDispatchMode dispatchMode = TaskCompletionDispatchMode.Dispatch) =>
        completer.ResolveAsync(
            companyId, sourceEntityId, source, actionType, null, completedBy, cancellationToken,
            dispatchMode == TaskCompletionDispatchMode.BusinessEffectAlreadyApplied);

    public async Task<bool> CancelBySourceEntitiesConfirmedAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        await canceller.CancelManyBySourceEntitiesAsync(companyId, sourceEntityIds, source, actionType, cancellationToken);
        return true;
    }
}
