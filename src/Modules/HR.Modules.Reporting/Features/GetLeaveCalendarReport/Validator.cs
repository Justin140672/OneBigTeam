using FluentValidation;

namespace HR.Modules.Reporting.Features.GetLeaveCalendarReport;

internal sealed class GetLeaveCalendarReportValidator : AbstractValidator<GetLeaveCalendarReportRequest>
{
    public GetLeaveCalendarReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
