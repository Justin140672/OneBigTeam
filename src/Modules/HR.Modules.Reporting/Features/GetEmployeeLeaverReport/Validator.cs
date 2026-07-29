using FluentValidation;

namespace HR.Modules.Reporting.Features.GetEmployeeLeaverReport;

internal sealed class GetEmployeeLeaverReportValidator : AbstractValidator<GetEmployeeLeaverReportRequest>
{
    public GetEmployeeLeaverReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.DateRangeEnd)
            .GreaterThanOrEqualTo(x => x.DateRangeStart!.Value)
            .When(x => x.DateRangeStart is not null && x.DateRangeEnd is not null);
    }
}
