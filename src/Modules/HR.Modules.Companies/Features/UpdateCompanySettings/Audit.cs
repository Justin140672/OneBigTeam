using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed record CompanySettingsAuditSnapshot(
    string TimeZone,
    string Locale);

internal sealed record CompanySettingsUpdatedAuditEvent(
    Guid CompanyId,
    string? ActorId,
    DateTimeOffset OccurredAt,
    CompanySettingsAuditSnapshot? PreviousSettings,
    CompanySettingsAuditSnapshot CurrentSettings) : IAuditEvent
{
    string IAuditEvent.EventType => "company-settings.updated";
    string IAuditEvent.EntityType => "CompanySettings";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => null;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Company settings updated";
    object? IAuditEvent.Before => PreviousSettings;
    object? IAuditEvent.After => CurrentSettings;
    object? IAuditEvent.Metadata => null;
}
