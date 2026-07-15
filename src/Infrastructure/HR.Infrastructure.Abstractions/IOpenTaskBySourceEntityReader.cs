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
    Task<IReadOnlyDictionary<Guid, Guid>> GetOpenTaskIdsAsync(
        Guid companyId,
        IEnumerable<Guid> sourceEntityIds,
        CancellationToken cancellationToken);
}
