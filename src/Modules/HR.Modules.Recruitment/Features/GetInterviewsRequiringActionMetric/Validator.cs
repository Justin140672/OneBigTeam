using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetInterviewsRequiringActionMetric;

internal sealed class GetInterviewsRequiringActionMetricValidator
    : AbstractValidator<GetInterviewsRequiringActionMetricRequest>
{
    public GetInterviewsRequiringActionMetricValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
