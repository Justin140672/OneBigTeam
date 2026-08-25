namespace HR.Modules.Tasks.Contracts;

// OFF-02: sibling of ITaskCanceller.CancelManyBySourceEntitiesAsync, for callers that need to
// shift due dates for a whole group of source entities (e.g. every OffboardingTask belonging to
// one OffboardingPlan) rather than cancel them — used when the underlying business date (e.g. an
// employee's last working day) is amended rather than the work being withdrawn.
public interface ITaskRescheduler
{
    /// <summary>
    /// Sets DueDate to <paramref name="newDueDate"/> for every open (not Completed, not
    /// Cancelled) TaskItem linked to any of <paramref name="sourceEntityIds"/>. Completed and
    /// Cancelled tasks are never touched, so historical/finished work keeps its original date.
    /// Only tasks whose DueDate actually changes are counted, notified and have their stale
    /// TaskDueSoon/TaskOverdue notifications cleared — calling this again with the same
    /// <paramref name="newDueDate"/> is therefore a safe no-op (idempotent).
    /// Each distinct assignee among the tasks that actually changed receives at most one
    /// "date changed" notification for this call, regardless of how many of their tasks moved.
    /// Returns the number of tasks actually rescheduled by this call.
    /// </summary>
    Task<int> RescheduleManyBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        DateOnly newDueDate,
        CancellationToken cancellationToken);
}
