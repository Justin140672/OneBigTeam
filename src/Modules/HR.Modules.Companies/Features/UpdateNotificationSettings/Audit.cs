using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateNotificationSettings;

internal sealed record NotificationSettingsAuditSnapshot(
    bool EmailNotificationsEnabled,
    bool ScheduledRemindersEnabled);

internal sealed record NotificationSettingsUpdatedAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    NotificationSettingsAuditSnapshot? PreviousSettings,
    NotificationSettingsAuditSnapshot CurrentSettings) : IAuditEvent
{
    string IAuditEvent.EventType => "notification-settings.updated";
    string IAuditEvent.EntityType => "CompanySettings";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Notification settings updated";
    object? IAuditEvent.Before => PreviousSettings;
    object? IAuditEvent.After => CurrentSettings;
    object? IAuditEvent.Metadata => null;
}
