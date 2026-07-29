using FluentValidation;

namespace HR.Modules.Reporting.Features.ExportLeaveCalendarReport;

internal sealed class ExportLeaveCalendarReportValidator : AbstractValidator<ExportLeaveCalendarReportRequest>
{
    public ExportLeaveCalendarReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Format).IsInEnum();
    }
}
