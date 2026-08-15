using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdatePlatformSettings;

internal sealed record PlatformSettingsAuditSnapshot(
    int TrialLengthDays,
    decimal DefaultMonthlyPriceGbp,
    string SupportEmail,
    bool MaintenanceModeEnabled,
    string? MaintenanceModeMessage,
    string FeatureFlagsJson);

/// <summary>
/// Records a platform-administrator change to the platform-wide settings singleton. Plugs into the
/// existing cross-cutting IAuditEventPublisher/AuditDbContext infrastructure already used by every
/// other module (see e.g. HR.Modules.Companies.Features.ExtendCustomerTrial.Audit) rather than a new
/// module-local audit table.
/// </summary>
internal sealed record PlatformSettingsUpdatedAuditEvent(
    Guid SettingsId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    PlatformSettingsAuditSnapshot? PreviousState,
    PlatformSettingsAuditSnapshot CurrentState) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "platform-settings.updated";
    string IAuditEvent.EntityType => "PlatformSettings";
    Guid IAuditEvent.EntityId => SettingsId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Platform settings updated by platform administrator.";
    object? IAuditEvent.Before => PreviousState;
    object? IAuditEvent.After => CurrentState;
    object? IAuditEvent.Metadata => null;
}
