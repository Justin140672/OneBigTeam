namespace HR.Modules.Tasks.Contracts;

public interface ITaskResolution
{
    /// <summary>
    /// Completes the open task and drives every completion effect to confirmation. Returns Outstanding
    /// when effects owned by another completion path (or another worker) are still outstanding, and
    /// TerminalFailure when a permanent failure is persisted; other unexpected effect failures throw.
    /// Dispatch failures are never ignored: <paramref name="dispatchMode"/> only selects whether the
    /// completion action runs or was already applied by the caller.
    /// </summary>
    Task<TaskResolutionResult> CompleteBySourceEntityConfirmedAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid completedBy,
        CancellationToken cancellationToken,
        TaskCompletionDispatchMode dispatchMode = TaskCompletionDispatchMode.Dispatch);

    Task<bool> CancelBySourceEntitiesConfirmedAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken);
}
