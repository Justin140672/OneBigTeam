using HR.SharedKernel;

namespace HR.Modules.Notifications.Jobs;

/// <summary>
/// NFR-07: records the outcome of a notifications retention sweep for one company. Deliberately
/// records only aggregate counts and the policy window — never the title, body or recipient of any
/// deleted notification. This is the "audit the deletion outcome, not the deleted content" pattern
/// (NFR-01). One event per company processed so retention activity is attributable per tenant.
/// </summary>
internal sealed record NotificationsRetentionRunAuditEvent(
    Guid CompanyId,
    DateTimeOffset OccurredAt,
    bool DryRun,
    int RetentionDays,
    DateTimeOffset Cutoff,
    int NotificationsDeleted,
    bool SkippedDueToLegalHold) : IAuditEvent
{
    AuditActorType IAuditEvent.ActorType => AuditActorType.ScheduledJob;
    string IAuditEvent.EventType => "notifications.retention-run";
    string IAuditEvent.EntityType => "Notification";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;

    string? IAuditEvent.Summary => SkippedDueToLegalHold
        ? $"PurgeExpiredReadNotificationsJob: skipped {NotificationsDeleted} read notification(s) older than {RetentionDays} days — company under legal hold."
        : DryRun
            ? $"PurgeExpiredReadNotificationsJob (dry run): would delete {NotificationsDeleted} read notification(s) older than {RetentionDays} days."
            : $"PurgeExpiredReadNotificationsJob: deleted {NotificationsDeleted} read notification(s) older than {RetentionDays} days.";

    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => null;
    object? IAuditEvent.Metadata => new
    {
        DryRun,
        RetentionDays,
        Cutoff,
        NotificationsDeleted,
        SkippedDueToLegalHold,
    };
}
