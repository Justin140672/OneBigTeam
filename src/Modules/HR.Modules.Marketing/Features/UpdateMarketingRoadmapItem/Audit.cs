using HR.SharedKernel;

namespace HR.Modules.Marketing.Features.UpdateMarketingRoadmapItem;

internal sealed record MarketingRoadmapItemAuditSnapshot(
    string Title,
    string Description,
    bool IsPublished,
    int DisplayOrder,
    string DeliveryStatus);

internal sealed record MarketingRoadmapItemUpdatedAuditEvent(
    Guid RoadmapItemId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    MarketingRoadmapItemAuditSnapshot PreviousState,
    MarketingRoadmapItemAuditSnapshot CurrentState) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "marketing-roadmap-item.updated";
    string IAuditEvent.EntityType => "MarketingRoadmapItem";
    Guid IAuditEvent.EntityId => RoadmapItemId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Marketing roadmap item updated by platform administrator.";
    object? IAuditEvent.Before => PreviousState;
    object? IAuditEvent.After => CurrentState;
    object? IAuditEvent.Metadata => null;
}
