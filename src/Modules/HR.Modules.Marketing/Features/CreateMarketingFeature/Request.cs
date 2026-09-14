using HR.Modules.Marketing.Domain;

namespace HR.Modules.Marketing.Features.CreateMarketingFeature;

internal sealed record CreateMarketingFeatureRequest(
    string Slug,
    string IconName,
    string Title,
    string Summary,
    string Intro,
    string? DetailedContent,
    IReadOnlyList<string>? Benefits,
    string? YouTubeId,
    int DisplayOrder,
    MarketingDeliveryStatus DeliveryStatus)
{
    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
