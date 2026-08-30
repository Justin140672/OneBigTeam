using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Notifications;

/// <summary>
/// ADM-03: raised whenever an administrative alert is raised or an existing live alert recurs.
/// Actor is the system (NotificationsSystemActor.Id) — alerts originate from background/compliance
/// infrastructure with no human at the call site.
/// </summary>
internal sealed record AdministrativeAlertRaisedAuditEvent(
    Guid CompanyId,
    Guid AlertId,
    AdministrativeAlertCategory Category,
    AdministrativeAlertSeverity Severity,
    string DedupKey,
    bool IsRecurrence,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "notifications.admin_alert_raised";
    string IAuditEvent.EntityType => "AdministrativeAlert";
    Guid IAuditEvent.EntityId => AlertId;
    Guid? IAuditEvent.EmployeeId => null;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => NotificationsSystemActor.Id;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => $"Administrative alert {(IsRecurrence ? "recurred" : "raised")} ({Category}, {Severity})";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Category, Severity, IsRecurrence };
    object? IAuditEvent.Metadata => null;
}

/// <summary>
/// ADM-03: raised when an admin acknowledges an open alert. Actor is the acknowledging user.
/// </summary>
internal sealed record AdministrativeAlertAcknowledgedAuditEvent(
    Guid CompanyId,
    Guid AlertId,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "notifications.admin_alert_acknowledged";
    string IAuditEvent.EntityType => "AdministrativeAlert";
    Guid IAuditEvent.EntityId => AlertId;
    Guid? IAuditEvent.EmployeeId => null;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Administrative alert acknowledged";
    object? IAuditEvent.Before => new { Status = "Open" };
    object? IAuditEvent.After => new { Status = "Acknowledged" };
    object? IAuditEvent.Metadata => null;
}

/// <summary>
/// ADM-03: raised when an admin resolves an alert. The free-text resolution note is never placed
/// in the audit payload (redaction guard) — only a HasNote flag.
/// </summary>
internal sealed record AdministrativeAlertResolvedAuditEvent(
    Guid CompanyId,
    Guid AlertId,
    Guid ActorUserId,
    string? ResolutionNote,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType => "notifications.admin_alert_resolved";
    string IAuditEvent.EntityType => "AdministrativeAlert";
    Guid IAuditEvent.EntityId => AlertId;
    Guid? IAuditEvent.EmployeeId => null;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Administrative alert resolved";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => new { Status = "Resolved", HasNote = ResolutionNote is not null };
    object? IAuditEvent.Metadata => null;
}
