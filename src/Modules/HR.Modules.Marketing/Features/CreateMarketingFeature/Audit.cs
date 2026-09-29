using HR.SharedKernel;

namespace HR.Modules.Marketing.Features.CreateMarketingFeature;

internal sealed record MarketingFeatureAuditSnapshot(
    string Slug,
    string Title,
    string Summary,
    bool IsPublished,
    int DisplayOrder,
    string DeliveryStatus);

internal sealed record MarketingFeatureCreatedAuditEvent(
    Guid FeatureId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    MarketingFeatureAuditSnapshot CurrentState) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "marketing-feature.created";
    string IAuditEvent.EntityType => "MarketingFeature";
    Guid IAuditEvent.EntityId => FeatureId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Marketing feature created by platform administrator.";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => CurrentState;
    object? IAuditEvent.Metadata => null;
}
