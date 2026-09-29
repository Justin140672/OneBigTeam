using HR.Modules.Recruitment.Features.DashboardMetrics;

namespace HR.Modules.Recruitment.Features.GetNewApplicationsMetric;

internal sealed record GetNewApplicationsMetricResponse(
    int Count,
    bool DefinedByStagePurpose,
    IReadOnlyList<RecruitmentMetricApplicationItem> Items);
