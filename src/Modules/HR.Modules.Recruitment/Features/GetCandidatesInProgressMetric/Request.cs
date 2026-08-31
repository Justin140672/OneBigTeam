namespace HR.Modules.Recruitment.Features.GetCandidatesInProgressMetric;

internal sealed record GetCandidatesInProgressMetricRequest
{
    public Guid CompanyId { get; init; }
}
