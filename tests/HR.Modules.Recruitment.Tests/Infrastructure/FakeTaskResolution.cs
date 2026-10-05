using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeTaskResolution(FakeTaskCompleter completer, FakeTaskCanceller canceller) : ITaskResolution
{
    public bool CompletionConfirmed { get; set; } = true;

    /// <summary>When set, overrides <see cref="CompletionConfirmed"/> (e.g. a terminal failure).</summary>
    public TaskResolutionResult? CompletionResult { get; set; }

    public List<TaskCompletionDispatchMode> CompletionModes { get; } = [];

    public async Task<TaskResolutionResult> CompleteBySourceEntityConfirmedAsync(
        Guid companyId, Guid sourceEntityId, TaskSource source, TaskActionType actionType, Guid completedBy,
        CancellationToken cancellationToken,
        TaskCompletionDispatchMode dispatchMode = TaskCompletionDispatchMode.Dispatch)
    {
        CompletionModes.Add(dispatchMode);
        await completer.CompleteBySourceEntityAsync(companyId, sourceEntityId, source, actionType, completedBy, cancellationToken);
        return CompletionResult
            ?? (CompletionConfirmed ? TaskResolutionResult.Confirmed() : TaskResolutionResult.Outstanding());
    }

    public async Task<bool> CancelBySourceEntitiesConfirmedAsync(
        Guid companyId, IReadOnlyCollection<Guid> sourceEntityIds, TaskSource source, TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        await canceller.CancelManyBySourceEntitiesAsync(companyId, sourceEntityIds, source, actionType, cancellationToken);
        return true;
    }
}
