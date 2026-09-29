using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdatePlatformSettings;

internal sealed record PlatformSettingsAuditSnapshot(
    int TrialLengthDays,
    decimal DefaultMonthlyPriceGbp,
    string SupportEmail,
    bool MaintenanceModeEnabled,
    string? MaintenanceModeMessage,
    string FeatureFlagsJson);

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
