using HR.Modules.Marketing.Domain;

namespace HR.Modules.Marketing.Features.UpdateMarketingRoadmapItem;

internal sealed record UpdateMarketingRoadmapItemRequest(
    Guid Id,
    string Title,
    string Description,
    string IconName,
    MarketingDeliveryStatus DeliveryStatus,
    int DisplayOrder);
