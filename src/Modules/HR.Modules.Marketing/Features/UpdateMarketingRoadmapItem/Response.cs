namespace HR.Modules.Marketing.Features.UpdateMarketingRoadmapItem;

internal sealed record UpdateMarketingRoadmapItemResponse(
    Guid Id,
    string Title,
    bool IsPublished,
    int DisplayOrder,
    string DeliveryStatus,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);
