namespace HR.Admin.Web.Models;

/// <summary>Mirrors ListMarketingContentResponse from HR.Modules.Marketing.</summary>
public sealed record AdminMarketingContentModel(
    AdminMarketingProductModel Product,
    IReadOnlyList<AdminMarketingFeatureModel> Features,
    IReadOnlyList<AdminMarketingRoadmapModel> Roadmap);

public sealed record AdminMarketingProductModel(
    Guid Id,
    string Name,
    string? Tagline,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);

public sealed record AdminMarketingFeatureModel(
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

public sealed record AdminMarketingRoadmapModel(
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

public sealed record CreateMarketingFeatureRequest(
    string Slug,
    string IconName,
    string Title,
    string Summary,
    string Intro,
    string? DetailedContent,
    IReadOnlyList<string>? Benefits,
    string? YouTubeId,
    int DisplayOrder,
    string DeliveryStatus);

public sealed record UpdateMarketingFeatureRequest(
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
    string DeliveryStatus);

public sealed record CreateMarketingRoadmapItemRequest(
    string Title,
    string Description,
    string IconName,
    string DeliveryStatus,
    int DisplayOrder);

public sealed record UpdateMarketingRoadmapItemRequest(
    Guid Id,
    string Title,
    string Description,
    string IconName,
    string DeliveryStatus,
    int DisplayOrder);

public sealed record MarketingMutationResult(bool Succeeded, IReadOnlyList<string>? Errors)
{
    public static readonly MarketingMutationResult Success = new(true, null);

    public static MarketingMutationResult Failure(params string[] errors) => new(false, errors);
}
