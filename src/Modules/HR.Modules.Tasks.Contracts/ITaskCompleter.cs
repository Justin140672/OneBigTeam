namespace HR.Modules.Tasks.Contracts;

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

    /// <summary>
    /// Finds the open task linked to <paramref name="sourceEntityId"/> that is assigned to
    /// <paramref name="assignedEmployeeId"/> and completes it. For source entities that fan out
    /// to one task per recipient (e.g. one Acknowledge task per eligible employee on a shared
    /// document), <see cref="CompleteBySourceEntityAsync"/> is unsafe to use — it would complete
    /// whichever matching task happens to be found first, which may belong to a different
    /// employee entirely. No-op if no matching task exists or it is already completed/cancelled.
    /// </summary>
    Task CompleteBySourceEntityForEmployeeAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid assignedEmployeeId,
        Guid completedBy,
        CancellationToken cancellationToken);
}
