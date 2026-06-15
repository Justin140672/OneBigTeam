using HR.SharedKernel;

namespace HR.Modules.Tasks;

internal sealed record TaskCreatedAuditEvent(
    Guid CompanyId,
    Guid TaskId,
    Guid CreatedBy,
    string Title,
    string Priority,
    string Source,
    Guid? AssignedEmployeeId,
    Guid? AssignedUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "task.created";
    string IAuditEvent.EntityType => "TaskItem";
    Guid IAuditEvent.EntityId => TaskId;
    Guid? IAuditEvent.ActorUserId => CreatedBy;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Task '{Title}' created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Title, Priority, Source, AssignedEmployeeId, AssignedUserId };
    object? IAuditEvent.Metadata => null;
}

internal sealed record TaskReassignedAuditEvent(
    Guid CompanyId,
    Guid TaskId,
    Guid? ActorUserId,
    Guid? PreviousEmployeeId,
    Guid? PreviousUserId,
    Guid? NewEmployeeId,
    Guid? NewUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "task.updated";
    string IAuditEvent.EntityType => "TaskItem";
    Guid IAuditEvent.EntityId => TaskId;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Task reassigned";
    object? IAuditEvent.Before => new { AssignedEmployeeId = PreviousEmployeeId, AssignedUserId = PreviousUserId };
    object? IAuditEvent.After => new { AssignedEmployeeId = NewEmployeeId, AssignedUserId = NewUserId };
    object? IAuditEvent.Metadata => null;
}

internal sealed record TaskCompletedAuditEvent(
    Guid CompanyId,
    Guid TaskId,
    Guid CompletedBy,
    string PreviousStatus,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "task.completed";
    string IAuditEvent.EntityType => "TaskItem";
    Guid IAuditEvent.EntityId => TaskId;
    Guid? IAuditEvent.ActorUserId => CompletedBy;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Task completed";
    object? IAuditEvent.Before => new { Status = PreviousStatus };
    object? IAuditEvent.After => new { Status = "Completed", CompletedBy };
    object? IAuditEvent.Metadata => null;
}
