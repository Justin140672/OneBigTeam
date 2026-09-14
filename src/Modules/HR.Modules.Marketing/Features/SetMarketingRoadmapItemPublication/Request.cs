namespace HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;

internal sealed record SetMarketingRoadmapItemPublicationRequest(
    Guid Id,
    bool IsPublished)
{
    internal string? IdempotencyKey { get; init; }
}
