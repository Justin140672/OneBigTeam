using HR.Modules.Marketing.Domain;

namespace HR.Modules.Marketing.Features.UpdateMarketingFeature;

internal sealed record UpdateMarketingFeatureRequest(
    Guid Id,
    string Slug,
    string IconName,
    string Title,
    string Summary,
    string Intro,
    string? DetailedContent,
    IReadOnlyList<string>? Benefits,
    string? YouTubeId,
    int DisplayOrder,
    MarketingDeliveryStatus DeliveryStatus);
