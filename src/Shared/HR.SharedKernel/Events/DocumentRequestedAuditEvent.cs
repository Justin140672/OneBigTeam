using HR.SharedKernel;

public sealed record DocumentRequestedAuditEvent(
    Guid CompanyId,
    Guid DocumentRequestId,
    Guid EmployeeId,
    string DocumentTypeName,
    DateOnly? DueDate,
    Guid RequestedBy,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string  IAuditEvent.EventType       => "document_request.created";
    string  IAuditEvent.EntityType      => "DocumentRequest";
    Guid    IAuditEvent.EntityId        => DocumentRequestId;
    Guid?   IAuditEvent.ActorUserId     => RequestedBy;
    Guid?   IAuditEvent.ActorEmployeeId => null;
    Guid?   IAuditEvent.CorrelationId   => null;
    string? IAuditEvent.Summary         => $"Document request for '{DocumentTypeName}' created";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { Status = "Requested", DocumentTypeName, DueDate, EmployeeId };
    object? IAuditEvent.Metadata        => null;
}
