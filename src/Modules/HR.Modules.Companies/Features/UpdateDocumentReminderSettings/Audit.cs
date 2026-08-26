using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateDocumentReminderSettings;

internal sealed record DocumentReminderSettingsAuditSnapshot(
    bool RemindersEnabled,
    int? OffsetDays1,
    int? OffsetDays2,
    int? OffsetDays3);

internal sealed record DocumentReminderSettingsUpdatedAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    DocumentReminderSettingsAuditSnapshot? PreviousSettings,
    DocumentReminderSettingsAuditSnapshot CurrentSettings) : IAuditEvent
{
    string IAuditEvent.EventType => "document-reminder-settings.updated";
    string IAuditEvent.EntityType => "CompanySettings";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Document reminder settings updated";
    object? IAuditEvent.Before => PreviousSettings;
    object? IAuditEvent.After => CurrentSettings;
    object? IAuditEvent.Metadata => null;
}
