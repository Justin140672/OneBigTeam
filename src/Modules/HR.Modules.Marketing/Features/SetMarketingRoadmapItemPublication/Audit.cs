using HR.SharedKernel;

namespace HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;

internal sealed record MarketingRoadmapItemPublicationChangedAuditEvent(
    Guid RoadmapItemId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    bool PreviouslyPublished,
    bool CurrentlyPublished) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "marketing-roadmap-item.publication-changed";
    string IAuditEvent.EntityType => "MarketingRoadmapItem";
    Guid IAuditEvent.EntityId => RoadmapItemId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => CurrentlyPublished
        ? "Marketing roadmap item published by platform administrator."
        : "Marketing roadmap item unpublished by platform administrator.";
    object? IAuditEvent.Before => new { IsPublished = PreviouslyPublished };
    object? IAuditEvent.After => new { IsPublished = CurrentlyPublished };
    object? IAuditEvent.Metadata => null;
}
