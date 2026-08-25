using HR.SharedKernel;

namespace HR.Modules.Assets;

internal sealed record AssetCreatedAuditEvent(
    Guid CompanyId,
    Guid AssetId,
    string AssetNumber,
    string Name,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "asset.created";
    string IAuditEvent.EntityType => "Asset";
    Guid IAuditEvent.EntityId => AssetId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Asset '{Name}' ({AssetNumber}) created";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { AssetNumber, Name };
    object? IAuditEvent.Metadata => null;
}

internal sealed record AssetAssignedAuditEvent(
    Guid CompanyId,
    Guid AssignmentId,
    Guid AssetId,
    Guid EmployeeId,
    Guid AssignedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "asset.assigned";
    string IAuditEvent.EntityType => "AssetAssignment";
    Guid IAuditEvent.EntityId => AssignmentId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => AssignedBy;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Asset assigned to employee";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { AssetId, EmployeeId, AssignedBy };
    object? IAuditEvent.Metadata => null;
}

internal sealed record AssetReturnRequestedAuditEvent(
    Guid CompanyId,
    Guid AssignmentId,
    Guid EmployeeId,
    Guid RequestedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "asset.return_requested";
    string IAuditEvent.EntityType => "AssetAssignment";
    Guid IAuditEvent.EntityId => AssignmentId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => RequestedBy;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Asset return requested";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { RequestedBy };
    object? IAuditEvent.Metadata => null;
}

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
    Guid? IAuditEvent.EmployeeId => EmployeeId;
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
    DateTimeOffset OccurredAt,
    string Outcome = "Returned",
    string? Notes = null) : IAuditEvent
{
    string IAuditEvent.EventType => "asset.assignment.returned";
    string IAuditEvent.EntityType => "AssetAssignment";
    Guid IAuditEvent.EntityId => AssignmentId;
    Guid? IAuditEvent.EmployeeId => EmployeeId;
    Guid? IAuditEvent.ActorUserId => ReturnedBy;
    Guid? IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => Outcome == "Returned" ? "Asset returned by employee" : $"Asset return recorded as {Outcome}";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { ReturnedBy, OccurredAt, Outcome };
    object? IAuditEvent.Metadata => Notes is null ? null : new { Notes };
}
