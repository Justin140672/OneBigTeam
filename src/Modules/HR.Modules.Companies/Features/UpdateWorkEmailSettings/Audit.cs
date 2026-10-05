using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateWorkEmailSettings;

internal sealed record WorkEmailSettingsAuditSnapshot(
    bool SuggestionsEnabled,
    string? PrimaryDomain,
    IReadOnlyList<string> AdditionalDomains,
    string NamingConvention);

internal sealed record WorkEmailSettingsUpdatedAuditEvent(
    Guid CompanyId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    WorkEmailSettingsAuditSnapshot? PreviousSettings,
    WorkEmailSettingsAuditSnapshot CurrentSettings) : IAuditEvent
{
    string IAuditEvent.EventType => "work-email-settings.updated";
    string IAuditEvent.EntityType => "CompanySettings";
    Guid IAuditEvent.EntityId => CompanyId;
    Guid? IAuditEvent.ActorUserId => ActorUserId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Work email settings updated";
    object? IAuditEvent.Before => PreviousSettings;
    object? IAuditEvent.After => CurrentSettings;
    object? IAuditEvent.Metadata => null;
}
