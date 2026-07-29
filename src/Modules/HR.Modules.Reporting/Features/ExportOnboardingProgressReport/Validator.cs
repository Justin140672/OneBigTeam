using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportOnboardingProgressReport;

internal sealed class ExportOnboardingProgressReportValidator : AbstractValidator<ExportOnboardingProgressReportRequest>
{
    public ExportOnboardingProgressReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
    }
}
