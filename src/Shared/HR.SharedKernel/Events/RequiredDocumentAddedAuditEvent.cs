namespace HR.SharedKernel;

public sealed record RequiredDocumentAddedAuditEvent(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid RequiredDocumentId,
    Guid DocumentTypeId,
    Guid ActorEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "position-profile.required-document.added";
    string IAuditEvent.EntityType => "PositionProfile";
    Guid IAuditEvent.EntityId => PositionProfileId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => ActorEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Required document added to position profile";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { RequiredDocumentId, DocumentTypeId };
    object? IAuditEvent.Metadata => null;
}
