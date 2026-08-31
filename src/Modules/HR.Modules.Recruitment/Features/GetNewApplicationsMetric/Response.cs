using HR.Modules.Recruitment.Features.DashboardMetrics;

namespace HR.Modules.Recruitment.Features.GetNewApplicationsMetric;

/// <summary>
/// DSH-04 authoritative "New applications" metric. <see cref="Count"/> always equals
/// <c>Items.Count</c> so the dashboard tile and its drill-down list can never disagree.
/// </summary>
internal sealed record GetNewApplicationsMetricResponse(
    int Count,
    bool DefinedByStagePurpose,
    IReadOnlyList<RecruitmentMetricApplicationItem> Items);
