using FluentValidation;

namespace HR.Modules.Reporting.Features.GetOffboardingProgressReport;

internal sealed class GetOffboardingProgressReportValidator : AbstractValidator<GetOffboardingProgressReportRequest>
{
    public GetOffboardingProgressReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
