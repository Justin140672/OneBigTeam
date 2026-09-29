using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.SharedKernel;

namespace HR.Modules.Notifications;

internal sealed record NotificationCreatedAuditEvent(
    Guid CompanyId,
    Guid NotificationId,
    Guid RecipientEmployeeId,
    NotificationType NotificationType,
    NotificationChannel Channel,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    Guid IAuditEvent.EventId => NotificationId;
    string IAuditEvent.EventType => "notifications.created";
    string IAuditEvent.EntityType => "Notification";
    Guid IAuditEvent.EntityId => NotificationId;
    Guid? IAuditEvent.EmployeeId => RecipientEmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => NotificationsSystemActor.Id;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Notification created ({NotificationType}, {Channel})";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { NotificationType, Channel };
    object? IAuditEvent.Metadata => null;
}

internal sealed record EmailDeliverySucceededAuditEvent(
    Guid CompanyId,
    Guid NotificationId,
    Guid RecipientEmployeeId,
    DateTimeOffset SentAt) : IAuditEvent
{
    string IAuditEvent.EventType => "notifications.email_delivery_succeeded";
    string IAuditEvent.EntityType => "EmailDelivery";
    Guid IAuditEvent.EntityId => NotificationId;
    Guid? IAuditEvent.EmployeeId => RecipientEmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => NotificationsSystemActor.Id;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Notification email delivered";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { SentAt };
    object? IAuditEvent.Metadata => null;
    DateTimeOffset IAuditEvent.OccurredAt => SentAt;
}

internal sealed record EmailDeliveryFailedAuditEvent(
    Guid CompanyId,
    Guid NotificationId,
    Guid RecipientEmployeeId,
    string SanitizedFailureReason,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "notifications.email_delivery_failed";
    string IAuditEvent.EntityType => "EmailDelivery";
    Guid IAuditEvent.EntityId => NotificationId;
    Guid? IAuditEvent.EmployeeId => RecipientEmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => NotificationsSystemActor.Id;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Notification email delivery permanently failed";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Reason = SanitizedFailureReason };
    object? IAuditEvent.Metadata => null;
}

internal sealed record NotificationReadAuditEvent(
    Guid CompanyId,
    Guid NotificationId,
    Guid RecipientEmployeeId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "notifications.read";
    string IAuditEvent.EntityType => "Notification";
    Guid IAuditEvent.EntityId => NotificationId;
    Guid? IAuditEvent.EmployeeId => RecipientEmployeeId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => RecipientEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Notification marked as read";
    object? IAuditEvent.Before => new { IsRead = false };
    object? IAuditEvent.After => new { IsRead = true };
    object? IAuditEvent.Metadata => null;
}
