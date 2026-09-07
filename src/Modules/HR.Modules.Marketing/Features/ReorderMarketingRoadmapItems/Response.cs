namespace HR.Modules.Marketing.Features.ReorderMarketingRoadmapItems;

internal sealed record ReorderMarketingRoadmapItemsResponse(
    IReadOnlyList<ReorderedMarketingRoadmapItemDto> Roadmap);

internal sealed record ReorderedMarketingRoadmapItemDto(
    Guid Id,
    int DisplayOrder);
