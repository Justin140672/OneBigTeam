namespace HR.Modules.Marketing.Features.ReorderMarketingFeatures;

internal sealed record ReorderMarketingFeaturesRequest(
    IReadOnlyList<Guid> OrderedIds);
