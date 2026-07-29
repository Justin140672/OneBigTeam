using FluentValidation;

namespace HR.Modules.Reporting.Features.GetEmployeeStarterReport;

internal sealed class GetEmployeeStarterReportValidator : AbstractValidator<GetEmployeeStarterReportRequest>
{
    public GetEmployeeStarterReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.DateRangeEnd)
            .GreaterThanOrEqualTo(x => x.DateRangeStart!.Value)
            .When(x => x.DateRangeStart is not null && x.DateRangeEnd is not null);
    }
}
