namespace HR.SharedKernel;

public sealed record RequiredDocumentRemovedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid RequiredDocumentId,
    Guid DocumentTypeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.required-document.removed";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Required document removed from position profile";
    object? IAuditEvent.Before => new { RequiredDocumentId, DocumentTypeId };
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => null;
}
