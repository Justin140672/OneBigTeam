using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportVacancyPerformanceReport;

internal sealed class ExportVacancyPerformanceReportValidator : AbstractValidator<ExportVacancyPerformanceReportRequest>
{
    public ExportVacancyPerformanceReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate is not null && x.EndDate is not null);
    }
}
