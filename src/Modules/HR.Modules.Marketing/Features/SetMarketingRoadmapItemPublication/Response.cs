namespace HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;

internal sealed record SetMarketingRoadmapItemPublicationResponse(
    Guid Id,
    bool IsPublished,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedByUserId);
