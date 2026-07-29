using FluentValidation;

namespace HR.Modules.Reporting.Features.GetOnboardingProgressReport;

internal sealed class GetOnboardingProgressReportValidator : AbstractValidator<GetOnboardingProgressReportRequest>
{
    public GetOnboardingProgressReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
