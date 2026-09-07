using HR.SharedKernel;

namespace HR.Modules.Marketing.Features.SetMarketingFeaturePublication;

internal sealed record MarketingFeaturePublicationChangedAuditEvent(
    Guid FeatureId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    bool PreviouslyPublished,
    bool CurrentlyPublished) : IAuditEvent
{
    Guid IAuditEvent.CompanyId => Guid.Empty;
    string IAuditEvent.EventType => "marketing-feature.publication-changed";
    string IAuditEvent.EntityType => "MarketingFeature";
    Guid IAuditEvent.EntityId => FeatureId;
    Guid? IAuditEvent.ActorEmployeeId => null;
    Guid? IAuditEvent.CorrelationId => null;
    string? IAuditEvent.Summary => CurrentlyPublished
        ? "Marketing feature published by platform administrator."
        : "Marketing feature unpublished by platform administrator.";
    object? IAuditEvent.Before => new { IsPublished = PreviouslyPublished };
    object? IAuditEvent.After => new { IsPublished = CurrentlyPublished };
    object? IAuditEvent.Metadata => null;
}
