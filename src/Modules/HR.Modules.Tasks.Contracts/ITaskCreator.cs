namespace HR.Modules.Tasks.Contracts;

public interface ITaskCreator
{
    // notifyAssignee: CreateAsync always writes a generic "New task assigned" notification when
    // assignedEmployeeId is set. Callers that are about to write their own, more specific
    // notification for that same assignee (e.g. "Acknowledgement required", "Asset assigned to
    // you") should pass false here, or the assignee ends up with two notifications for one event.
    //
    // idempotencyKey (OBT-REM-13): optional, deterministic key identifying the underlying
    // occurrence a workflow-created task represents (e.g. "SicknessEvidenceOverdue:{requestId}").
    // When supplied, creation is idempotent under retries and concurrent callers — a second call
    // with the same (company, key) pair is a no-op that returns the existing task's Id rather than
    // creating a duplicate, enforced by a database unique constraint rather than relying solely on
    // a caller's own read-before-create check. Leave null (the default) for tasks with no
    // replay/duplication concern to guard against — most callers.
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
        bool notifyAssignee = true,
        string? idempotencyKey = null);
}
