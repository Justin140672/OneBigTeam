namespace HR.Modules.Tasks.Contracts;

public interface ITaskReassigner
{
    Task<int> ReassignAllByAssigneeAsync(
        Guid companyId,
        Guid fromEmployeeId,
        Guid? toEmployeeId,
        CancellationToken cancellationToken);

    Task<int> ReassignBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        Guid toEmployeeId,
        CancellationToken cancellationToken);
}
