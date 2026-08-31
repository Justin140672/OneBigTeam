namespace HR.Modules.Recruitment.Features.GetInterviewsRequiringActionMetric;

internal sealed record GetInterviewsRequiringActionMetricRequest
{
    public Guid CompanyId { get; init; }
}
