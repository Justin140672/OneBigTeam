using HR.SharedKernel;

namespace HR.Modules.Assets;

internal sealed record AssetAssignmentAcknowledgedAuditEvent(
    Guid CompanyId,
    Guid AssignmentId,
    Guid AssetId,
    Guid EmployeeId,
    Guid AcknowledgedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "asset.assignment.acknowledged";
    string IAuditEvent.EntityType => "AssetAssignment";
    Guid IAuditEvent.EntityId => AssignmentId;
    Guid? IAuditEvent.ActorUserId => AcknowledgedBy;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Asset assignment acknowledged by employee";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { AcknowledgedBy, OccurredAt };
    object? IAuditEvent.Metadata => null;
}

internal sealed record AssetAssignmentReturnedAuditEvent(
    Guid CompanyId,
    Guid AssignmentId,
    Guid AssetId,
    Guid EmployeeId,
    Guid ReturnedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "asset.assignment.returned";
    string IAuditEvent.EntityType => "AssetAssignment";
    Guid IAuditEvent.EntityId => AssignmentId;
    Guid? IAuditEvent.ActorUserId => ReturnedBy;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Asset returned by employee";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { ReturnedBy, OccurredAt };
    object? IAuditEvent.Metadata => null;
}
