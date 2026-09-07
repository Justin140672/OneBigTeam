using HR.SharedKernel;

namespace HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;

internal sealed record MarketingRoadmapItemAuditSnapshot(
    string Title,
    string Description,
    bool IsPublished,
    int DisplayOrder,
    string DeliveryStatus);

internal sealed record MarketingRoadmapItemCreatedAuditEvent(
    Guid RoadmapItemId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    MarketingRoadmapItemAuditSnapshot CurrentState) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "marketing-roadmap-item.created";
    string IAuditEvent.EntityType => "MarketingRoadmapItem";
    Guid IAuditEvent.EntityId => RoadmapItemId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Marketing roadmap item created by platform administrator.";
    object? IAuditEvent.Before => null;
    object? IAuditEvent.After => CurrentState;
    object? IAuditEvent.Metadata => null;
}
