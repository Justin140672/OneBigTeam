namespace HR.Modules.Marketing.Features.ReorderMarketingFeatures;

internal sealed record ReorderMarketingFeaturesRequest(
    IReadOnlyList<Guid> OrderedIds)
{
    internal string? IdempotencyKey { get; init; }
}
