namespace HR.Modules.Marketing.Features.UpdateMarketingFeature;

internal sealed record UpdateMarketingFeatureResponse(
    Guid Id,
    string Slug,
    string Title,
    bool IsPublished,
    int DisplayOrder,
    string DeliveryStatus,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);
