using HR.Modules.Recruitment.Features.DashboardMetrics;

namespace HR.Modules.Recruitment.Features.GetCandidatesInProgressMetric;

internal sealed record GetCandidatesInProgressMetricResponse(
    int Count,
    IReadOnlyList<RecruitmentMetricApplicationItem> Items);
