namespace HR.Modules.Tasks.Contracts;

public sealed record TaskLink(Guid TaskId, Guid? AssignedEmployeeId, Guid? AssignedUserId, bool IsOpen);

/// <summary>
/// Lets other modules resolve the Task that the Tasks module created for one of their own
/// entities, including tasks that are already completed or cancelled so history rows can still
/// link to them. Prefers the open task; otherwise the most recently created one.
/// </summary>
public interface ITaskLinkBySourceEntityReader
{
    Task<IReadOnlyDictionary<Guid, TaskLink>> GetTaskLinksAsync(
        Guid companyId,
        IEnumerable<Guid> sourceEntityIds,
        CancellationToken cancellationToken,
        TaskActionType? actionType = null);
}
