using HR.Modules.Marketing.Domain;

namespace HR.Modules.Marketing.Features.CreateMarketingFeature;

internal sealed record CreateMarketingFeatureRequest(
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
