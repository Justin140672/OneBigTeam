using HR.SharedKernel;

namespace HR.Modules.Marketing.Features.UpdateMarketingFeature;

internal sealed record MarketingFeatureAuditSnapshot(
    string Slug,
    string Title,
    string Summary,
    bool IsPublished,
    int DisplayOrder,
    string DeliveryStatus);

internal sealed record MarketingFeatureUpdatedAuditEvent(
    Guid FeatureId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    MarketingFeatureAuditSnapshot PreviousState,
    MarketingFeatureAuditSnapshot CurrentState) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "marketing-feature.updated";
    string IAuditEvent.EntityType => "MarketingFeature";
    Guid IAuditEvent.EntityId => FeatureId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Marketing feature updated by platform administrator.";
    object? IAuditEvent.Before => PreviousState;
    object? IAuditEvent.After => CurrentState;
    object? IAuditEvent.Metadata => null;
}
