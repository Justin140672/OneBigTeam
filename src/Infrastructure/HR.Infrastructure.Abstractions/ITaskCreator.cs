namespace HR.Infrastructure.Abstractions;

public interface ITaskCreator
{
    // notifyAssignee: CreateAsync always writes a generic "New task assigned" notification when
    // assignedEmployeeId is set. Callers that are about to write their own, more specific
    // notification for that same assignee (e.g. "Acknowledgement required", "Asset assigned to
    // you") should pass false here, or the assignee ends up with two notifications for one event.
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
        CancellationToken cancellationToken,
        bool notifyAssignee = true);
}
