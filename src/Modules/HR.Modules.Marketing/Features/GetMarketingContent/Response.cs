namespace HR.Modules.Marketing.Features.GetMarketingContent;

internal sealed record GetMarketingContentResponse(
    MarketingContentProductDto Product,
    IReadOnlyList<MarketingContentFeatureDto> Features,
    IReadOnlyList<MarketingContentRoadmapDto> Roadmap);

internal sealed record MarketingContentProductDto(
    string Name,
    string? Tagline);

internal sealed record MarketingContentFeatureDto(
    string Slug,
    string IconName,
    string Title,
    string Summary,
    string Intro,
    string? DetailedContent,
    IReadOnlyList<string> Benefits,
    string? YouTubeId,
    string DeliveryStatus);

internal sealed record MarketingContentRoadmapDto(
    string Title,
    string Description,
    string IconName,
    string DeliveryStatus);
