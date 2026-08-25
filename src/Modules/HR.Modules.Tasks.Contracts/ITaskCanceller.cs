namespace HR.Modules.Tasks.Contracts;

public interface ITaskCanceller
{
    /// <summary>
    /// Finds the first open task linked to <paramref name="sourceEntityId"/> and cancels it.
    /// No-op if no matching task exists or it is already completed/cancelled.
    /// </summary>
    Task CancelBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cancels every open task linked to <paramref name="sourceEntityId"/> — unlike
    /// <see cref="CancelBySourceEntityAsync"/>, which only cancels the first match, this is for
    /// source entities that fan out to one task per recipient (e.g. one Acknowledge task per
    /// eligible employee on a shared document). Returns the number of tasks cancelled.
    /// </summary>
    Task<int> CancelAllBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken);

    /// <summary>
    /// OFF-01: bulk cancel-by-source, for callers that own a whole group of source entities
    /// (e.g. every OffboardingTask belonging to one OffboardingPlan) rather than a single
    /// source entity id. Unlike <see cref="CancelAllBySourceEntityAsync"/> (many tasks fanned
    /// out from one shared source entity id), this is for many *different* source entity ids
    /// that each own at most a handful of tasks — the correlation is the caller's own grouping
    /// (e.g. plan membership), not a single shared SourceEntityId column value.
    /// Excludes tasks already Completed or Cancelled (terminal states are never touched), so
    /// this is safe to call repeatedly against the same set of ids — already-cancelled/-completed
    /// tasks are silently skipped, making this idempotent and safe as a reconciliation retry.
    /// Also removes any pending TaskDueSoon/TaskOverdue notifications tied to a task that gets
    /// cancelled by this call, and — since cancelling moves the task's status away from
    /// Open/InProgress — automatically stops future overdue reminders for it too (DueSoonNotifier
    /// only considers Open/InProgress tasks).
    /// Returns the number of tasks actually cancelled by this call.
    /// </summary>
    Task<int> CancelManyBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken);
}
