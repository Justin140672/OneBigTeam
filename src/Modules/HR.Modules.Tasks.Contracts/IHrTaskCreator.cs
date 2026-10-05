namespace HR.Modules.Tasks.Contracts;

public interface IHrTaskCreator
{
    Task<Guid> CreateForHrAsync(
        Guid companyId,
        Guid createdBy,
        string title,
        string? description,
        TaskPriority priority,
        TaskSource source,
        TaskActionType actionType,
        DateOnly? dueDate,
        Guid? sourceEntityId,
        CancellationToken cancellationToken);
}
