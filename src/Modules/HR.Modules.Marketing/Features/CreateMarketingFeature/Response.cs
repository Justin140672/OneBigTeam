namespace HR.Modules.Marketing.Features.CreateMarketingFeature;

internal sealed record CreateMarketingFeatureResponse(
    Guid Id,
    string Slug,
    string Title,
    bool IsPublished,
    int DisplayOrder,
    string DeliveryStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
