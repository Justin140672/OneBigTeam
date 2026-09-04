using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.SharedKernel;

namespace HR.Modules.Notifications;

/// <summary>
/// NOT-05: raised whenever a notification is created (both the templated NOT-03 write path and the
/// pre-existing free-text WriteAsync path used by 30+ handlers/jobs across the app). Actor is
/// NotificationsSystemActor.Id — notification creation is shared infrastructure invoked from deep
/// inside other modules' handlers, which have no reliable way to thread a human actor id through to
/// this call site; the human decision that ultimately caused the notification (e.g. "manager
/// approved leave") is already audited by that originating module's own audit event.
/// </summary>
internal sealed record NotificationCreatedAuditEvent(
    Guid CompanyId,
    Guid NotificationId,
    Guid RecipientEmployeeId,
    NotificationType NotificationType,
    NotificationChannel Channel,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    // OBT-REM-12: deterministic EventId derived from the notification id — exactly one
    // NotificationCreatedAuditEvent is ever meaningful per notification, so reusing NotificationId
    // as the idempotency key means republishing this event (e.g. from
    // ReconcileMissingNotificationAuditsJob, or the duplicate-request repair path in
    // NotificationWriter) can never create a duplicate audit row: DbAuditEventPublisher /
    // AuditPendingItemPromotionJob both dedupe on EventId via a unique index.
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

/// <summary>
/// NOT-05: raised the moment EmailDeliveryJob's Postmark call succeeds (EmailDelivery.MarkSent).
/// Never raised on the idempotency no-op path (delivery already Sent) — see EmailDeliveryJob.SendAsync
/// — so a replayed/re-enqueued job for an already-delivered notification does not produce a second,
/// misleading success event.
/// </summary>
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

/// <summary>
/// NOT-05: raised only on the FINAL exhausted-retry failure (see EmailDeliveryJob's
/// isFinalAttempt branch and the "no recipient email on file" immediate-fail path) — never on an
/// intermediate transient-retry attempt, so a delivery that eventually succeeds after one or more
/// retries never produces a failure event. Reason reuses the same sanitised category string already
/// produced by EmailDeliveryJob.SanitizeFailureReason / EmailDelivery.FailureReason — this is the
/// only sanitisation path; raw exception/provider detail never reaches this event.
/// </summary>
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
    // NOT-05: SanitizedFailureReason is already a short, non-sensitive category (never raw
    // exception/provider detail) — see EmailDeliveryJob.SanitizeFailureReason.
    object? IAuditEvent.After => new { Reason = SanitizedFailureReason };
    object? IAuditEvent.Metadata => null;
}

/// <summary>
/// NOT-05: raised for both the individual MarkNotificationRead flow and, once per notification,
/// for bulk MarkAllNotificationsRead — mirroring the per-entity audit convention used elsewhere in
/// this session (e.g. SicknessRecordedAuditEvent/SicknessClosedAuditEvent are one event per record,
/// not a single aggregated "N records changed" event) rather than introducing a new batched-event
/// shape. Only raised on an actual unread-to-read transition (mirrors PROB-07/OFF-05's
/// idempotent-audit pattern) — marking an already-read notification as read again, or a bulk
/// mark-all-read that finds nothing unread, produces no events.
/// </summary>
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
    // NOT-05: unlike notification creation/delivery, marking-as-read is always a direct human
    // action by the recipient themselves — actor and recipient are the same employee.
    Guid? IAuditEvent.ActorEmployeeId => RecipientEmployeeId;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Notification marked as read";
    object? IAuditEvent.Before => new { IsRead = false };
    object? IAuditEvent.After => new { IsRead = true };
    object? IAuditEvent.Metadata => null;
}
