namespace HR.Modules.Tasks.Contracts;

public interface ITaskCompleter
{
    Task CompleteBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid completedBy,
        CancellationToken cancellationToken);

    Task CompleteBySourceEntityForEmployeeAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid assignedEmployeeId,
        Guid completedBy,
        CancellationToken cancellationToken);
}
