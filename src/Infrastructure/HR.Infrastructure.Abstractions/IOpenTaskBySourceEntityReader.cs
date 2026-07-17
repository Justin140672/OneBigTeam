namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Lets other modules resolve the open (not Completed/Cancelled) Task, if any, that the Tasks
/// module created for one of their own entities — e.g. the leave-approval task created for a
/// LeaveRequest via SourceEntityId. Read-only, company-scoped, and intentionally narrow: it
/// only reports the single most relevant open task per source entity id, not full task details.
/// </summary>
public interface IOpenTaskBySourceEntityReader
{
    /// <summary>
    /// Returns a sourceEntityId -&gt; open TaskItem id map for the given source entity ids.
    /// Source entities with no open task (none created, or already completed/cancelled) are
    /// omitted from the result rather than mapped to null.
    /// </summary>
    /// <param name="actionType">
    /// Optional filter to only consider open tasks of a specific <see cref="TaskActionType"/>.
    /// Defaults to null, which preserves the original behaviour of matching any open task
    /// regardless of action type — required because a single source entity can have multiple,
    /// concurrent open tasks of different action types (e.g. a Shared Company Document can have
    /// many open Acknowledge tasks, one per eligible employee, alongside at most one open Review
    /// task). Callers that only care about one specific kind of task for a source entity (e.g.
    /// "does this document already have an open Review task?") must supply this to avoid an
    /// unrelated open task of a different action type being mistaken for the one they're checking.
    /// </param>
    Task<IReadOnlyDictionary<Guid, Guid>> GetOpenTaskIdsAsync(
        Guid companyId,
        IEnumerable<Guid> sourceEntityIds,
        CancellationToken cancellationToken,
        TaskActionType? actionType = null);
}
