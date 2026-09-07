namespace HR.Modules.Marketing.Features.ReorderMarketingRoadmapItems;

internal sealed record ReorderMarketingRoadmapItemsRequest(
    IReadOnlyList<Guid> OrderedIds);
