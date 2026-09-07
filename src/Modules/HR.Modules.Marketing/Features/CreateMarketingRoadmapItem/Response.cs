namespace HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;

internal sealed record CreateMarketingRoadmapItemResponse(
    Guid Id,
    string Title,
    bool IsPublished,
    int DisplayOrder,
    string DeliveryStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
