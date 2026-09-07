using HR.Modules.Marketing.Domain;

namespace HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;

internal sealed record CreateMarketingRoadmapItemRequest(
    string Title,
    string Description,
    string IconName,
    MarketingDeliveryStatus DeliveryStatus,
    int DisplayOrder);
