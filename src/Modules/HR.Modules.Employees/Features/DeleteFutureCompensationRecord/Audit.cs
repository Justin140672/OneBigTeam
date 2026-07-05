using HR.SharedKernel;

namespace HR.Modules.Employees.Features.DeleteFutureCompensationRecord;

internal sealed record CompensationRecordDeletedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    DateOnly EffectiveFrom,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.deleted";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record deleted";
    object? IAuditEvent.Before => new { EffectiveFrom };
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}

internal sealed record CompensationRecordReopenedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid CompensationRecordId,
    DateOnly EffectiveFrom,
    DateOnly PreviousEffectiveTo,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "employee.compensation.reopened";
    string IAuditEvent.EntityType => "Compensation";
    Guid IAuditEvent.EntityId => CompensationRecordId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Compensation record reopened after deletion of its successor";
    object? IAuditEvent.Before => new { EffectiveTo = PreviousEffectiveTo };
    object? IAuditEvent.After => new { EffectiveTo = (DateOnly?)null };
    object? IAuditEvent.Metadata => null;
}
