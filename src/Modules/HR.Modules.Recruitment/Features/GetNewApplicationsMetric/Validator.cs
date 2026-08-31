using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetNewApplicationsMetric;

internal sealed class GetNewApplicationsMetricValidator : AbstractValidator<GetNewApplicationsMetricRequest>
{
    public GetNewApplicationsMetricValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.NewWithinDays).GreaterThan(0).When(r => r.NewWithinDays.HasValue);
    }
}
