using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportOffboardingProgressReport;

internal sealed class ExportOffboardingProgressReportValidator : AbstractValidator<ExportOffboardingProgressReportRequest>
{
    public ExportOffboardingProgressReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
    }
}
