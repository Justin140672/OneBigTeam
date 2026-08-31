using HR.Modules.Recruitment.Features.DashboardMetrics;

namespace HR.Modules.Recruitment.Features.GetOffersAwaitingResponseMetric;

/// <summary>
/// DSH-04 authoritative "Offers awaiting response" metric. <see cref="Count"/> always equals
/// <c>Items.Count</c>. <see cref="OfferStageConfigured"/> is <c>false</c> when the company has not
/// flagged any stage with the Offer purpose, in which case the count is 0 and the UI should prompt
/// the company to configure one (rather than guessing from stage order).
/// </summary>
internal sealed record GetOffersAwaitingResponseMetricResponse(
    int Count,
    bool OfferStageConfigured,
    IReadOnlyList<RecruitmentMetricApplicationItem> Items);
