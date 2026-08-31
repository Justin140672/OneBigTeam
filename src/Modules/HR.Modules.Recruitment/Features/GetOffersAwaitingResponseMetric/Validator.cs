using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetOffersAwaitingResponseMetric;

internal sealed class GetOffersAwaitingResponseMetricValidator : AbstractValidator<GetOffersAwaitingResponseMetricRequest>
{
    public GetOffersAwaitingResponseMetricValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
