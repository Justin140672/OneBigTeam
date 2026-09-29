namespace HR.Modules.Tasks.Contracts;

public interface ITaskRescheduler
{
    Task<int> RescheduleManyBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        DateOnly newDueDate,
        CancellationToken cancellationToken);
}
