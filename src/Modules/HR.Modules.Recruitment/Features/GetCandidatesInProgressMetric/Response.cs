using HR.Modules.Recruitment.Features.DashboardMetrics;

namespace HR.Modules.Recruitment.Features.GetCandidatesInProgressMetric;

/// <summary>
/// DSH-04 authoritative "Candidates in progress" metric. <see cref="Count"/> always equals
/// <c>Items.Count</c>.
/// </summary>
internal sealed record GetCandidatesInProgressMetricResponse(
    int Count,
    IReadOnlyList<RecruitmentMetricApplicationItem> Items);
