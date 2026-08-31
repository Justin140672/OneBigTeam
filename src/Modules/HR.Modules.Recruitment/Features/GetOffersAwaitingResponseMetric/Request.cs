namespace HR.Modules.Recruitment.Features.GetOffersAwaitingResponseMetric;

internal sealed record GetOffersAwaitingResponseMetricRequest
{
    public Guid CompanyId { get; init; }
}
