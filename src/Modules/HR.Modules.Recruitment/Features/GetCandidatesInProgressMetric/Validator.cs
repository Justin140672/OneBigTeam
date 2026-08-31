using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetCandidatesInProgressMetric;

internal sealed class GetCandidatesInProgressMetricValidator : AbstractValidator<GetCandidatesInProgressMetricRequest>
{
    public GetCandidatesInProgressMetricValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
