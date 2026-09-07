using HR.SharedKernel;

namespace HR.Modules.Marketing.Features.ReorderMarketingFeatures;

internal sealed record MarketingFeaturesReorderedAuditEvent(
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<Guid> PreviousOrder,
    IReadOnlyList<Guid> CurrentOrder) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "marketing-features.reordered";
    string IAuditEvent.EntityType => "MarketingFeature";
    Guid IAuditEvent.EntityId => Guid.Empty;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => "Marketing features reordered by platform administrator.";
    object? IAuditEvent.Before => new { Order = PreviousOrder };
    object? IAuditEvent.After => new { Order = CurrentOrder };
    object? IAuditEvent.Metadata => null;
}
