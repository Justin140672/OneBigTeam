namespace HR.Modules.Marketing.Features.ReorderMarketingFeatures;

internal sealed record ReorderMarketingFeaturesResponse(
    IReadOnlyList<ReorderedMarketingFeatureDto> Features);

internal sealed record ReorderedMarketingFeatureDto(
    Guid Id,
    int DisplayOrder);
