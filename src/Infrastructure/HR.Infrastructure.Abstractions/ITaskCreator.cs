namespace HR.Infrastructure.Abstractions;

public interface ITaskCreator
{
    Task<Guid> CreateAsync(
        Guid companyId,
        Guid createdBy,
        string title,
        string? description,
        TaskPriority priority,
        TaskSource source,
        TaskActionType actionType,
        DateOnly? dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId,
        Guid? sourceEntityId,
        CancellationToken cancellationToken);
}
