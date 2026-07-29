using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportLeaveSummaryReport;

internal sealed class ExportLeaveSummaryReportValidator : AbstractValidator<ExportLeaveSummaryReportRequest>
{
    public ExportLeaveSummaryReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Format).IsInEnum();
        RuleFor(x => x.GroupBy).IsInEnum();
        RuleFor(x => x.PolicyYear).InclusiveBetween(2000, 2100).When(x => x.PolicyYear is not null);
    }
}
