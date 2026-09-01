using HR.SharedKernel;

namespace HR.Modules.Companies.Features.UpdateSubscriptionPricingConfig;

internal sealed record SubscriptionPricingConfigAuditSnapshot(
    string BandsJson,
    decimal MinimumMonthlyChargeGbp);

/// <summary>
/// Records a platform-administrator change to the configurable subscription pricing model
/// (Story 4). Plugs into the existing cross-cutting IAuditEventPublisher/AuditDbContext
/// infrastructure, mirroring PlatformSettingsUpdatedAuditEvent.
/// </summary>
internal sealed record SubscriptionPricingConfigUpdatedAuditEvent(
    Guid SettingsId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    SubscriptionPricingConfigAuditSnapshot? PreviousState,
    SubscriptionPricingConfigAuditSnapshot CurrentState) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "platform-settings.subscription-pricing.updated";
    string IAuditEvent.EntityType => "PlatformSettings";
    Guid IAuditEvent.EntityId => SettingsId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Subscription pricing configuration updated by platform administrator.";
    object? IAuditEvent.Before => PreviousState;
    object? IAuditEvent.After => CurrentState;
    object? IAuditEvent.Metadata => null;
}
