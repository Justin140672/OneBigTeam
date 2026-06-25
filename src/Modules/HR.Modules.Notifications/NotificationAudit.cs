using HR.SharedKernel;

namespace HR.Modules.Notifications;

internal sealed record NotificationCreatedAuditEvent(
    Guid CompanyId,
    Guid NotificationId,
    Guid EmployeeId,
    string Title,
    string Type,
    string Priority,
    Guid SourceEntityId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType      => "notification.created";
    string IAuditEvent.EntityType     => "Notification";
    Guid   IAuditEvent.EntityId       => NotificationId;
    Guid?  IAuditEvent.ActorUserId    => null;
    Guid?  IAuditEvent.ActorEmployeeId => EmployeeId;
    Guid?  IAuditEvent.CorrelationId  => null;
    string? IAuditEvent.Summary       => $"Notification '{Title}' sent to employee";
    object? IAuditEvent.Before        => null;
    object? IAuditEvent.After         => new { Type, Priority, SourceEntityId };
    object? IAuditEvent.Metadata      => null;
}
