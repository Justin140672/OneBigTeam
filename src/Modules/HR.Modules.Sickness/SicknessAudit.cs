using HR.SharedKernel;

namespace HR.Modules.Sickness;

internal sealed record SicknessUpdatedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? TotalDays,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.updated";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record updated";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { CategoryId, StartDate, EndDate, TotalDays };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessClosedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal? TotalDays,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.closed";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record closed";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { CategoryId, StartDate, EndDate, TotalDays };
    object? IAuditEvent.Metadata => null;
}

internal sealed record SicknessRecordedAuditEvent(
    Guid CompanyId,
    Guid EmployeeId,
    Guid SicknessRecordId,
    Guid CategoryId,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? TotalDays,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "sickness.recorded";
    string IAuditEvent.EntityType => "SicknessRecord";
    Guid IAuditEvent.EntityId => SicknessRecordId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Sickness record created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { CategoryId, StartDate, EndDate, TotalDays };
    object? IAuditEvent.Metadata => null;
}
