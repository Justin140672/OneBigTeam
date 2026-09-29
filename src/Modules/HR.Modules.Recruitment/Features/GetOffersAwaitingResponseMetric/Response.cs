using HR.Modules.Recruitment.Features.DashboardMetrics;

namespace HR.Modules.Recruitment.Features.GetOffersAwaitingResponseMetric;

internal sealed record GetOffersAwaitingResponseMetricResponse(
    int Count,
    bool OfferStageConfigured,
    IReadOnlyList<RecruitmentMetricApplicationItem> Items);
