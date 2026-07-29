using FluentValidation;

namespace HR.Modules.Reporting.Features.GetLeaveSummaryReport;

internal sealed class GetLeaveSummaryReportValidator : AbstractValidator<GetLeaveSummaryReportRequest>
{
    public GetLeaveSummaryReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.GroupBy).IsInEnum();
        RuleFor(x => x.PolicyYear).InclusiveBetween(2000, 2100).When(x => x.PolicyYear is not null);
    }
}
