namespace HR.SharedKernel;

public interface ITaskCompleter
{
    /// <summary>
    /// Finds the first open task linked to <paramref name="sourceEntityId"/> and completes it.
    /// No-op if no matching task exists or it is already completed/cancelled.
    /// </summary>
    Task CompleteBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid completedBy,
        CancellationToken cancellationToken);
}
