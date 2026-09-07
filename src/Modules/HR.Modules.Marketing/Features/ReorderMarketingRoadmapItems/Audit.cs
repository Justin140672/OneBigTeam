using HR.SharedKernel;

namespace HR.Modules.Marketing.Features.ReorderMarketingRoadmapItems;

internal sealed record MarketingRoadmapItemsReorderedAuditEvent(
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<Guid> PreviousOrder,
    IReadOnlyList<Guid> CurrentOrder) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "marketing-roadmap-items.reordered";
    string IAuditEvent.EntityType => "MarketingRoadmapItem";
    Guid IAuditEvent.EntityId => Guid.Empty;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Marketing roadmap items reordered by platform administrator.";
    object? IAuditEvent.Before => new { Order = PreviousOrder };
    object? IAuditEvent.After => new { Order = CurrentOrder };
    object? IAuditEvent.Metadata => null;
}
