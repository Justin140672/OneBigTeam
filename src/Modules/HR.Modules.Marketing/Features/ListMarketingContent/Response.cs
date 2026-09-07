namespace HR.Modules.Marketing.Features.ListMarketingContent;

internal sealed record ListMarketingContentResponse(
    AdminMarketingProductDto Product,
    IReadOnlyList<AdminMarketingFeatureDto> Features,
    IReadOnlyList<AdminMarketingRoadmapDto> Roadmap);

internal sealed record AdminMarketingProductDto(
    Guid Id,
    string Name,
    string? Tagline,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);

internal sealed record AdminMarketingFeatureDto(
    Guid Id,
    string Slug,
    string IconName,
    string Title,
    string Summary,
    string Intro,
    string? DetailedContent,
    IReadOnlyList<string> Benefits,
    string? YouTubeId,
    int DisplayOrder,
    bool IsPublished,
    string DeliveryStatus,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);

internal sealed record AdminMarketingRoadmapDto(
    Guid Id,
    string Title,
    string Description,
    string IconName,
    int DisplayOrder,
    bool IsPublished,
    string DeliveryStatus,
    DateTimeOffset CreatedAt,
    Guid? CreatedByUserId,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);
