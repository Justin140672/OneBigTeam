using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportHrHeadcountSummaryReport;

internal sealed class ExportHrHeadcountSummaryReportValidator : AbstractValidator<ExportHrHeadcountSummaryReportRequest>
{
    public ExportHrHeadcountSummaryReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
    }
}
