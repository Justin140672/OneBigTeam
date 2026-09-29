namespace HR.Modules.Recruitment.Features.GetNewApplicationsMetric;

internal sealed record GetNewApplicationsMetricRequest
{
    public Guid CompanyId { get; init; }

    public int? NewWithinDays { get; init; }
}
