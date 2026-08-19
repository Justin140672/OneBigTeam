using FluentValidation;

namespace HR.Modules.Reporting.Features.GetHrHeadcountSummaryReport;

internal sealed class GetHrHeadcountSummaryReportValidator : AbstractValidator<GetHrHeadcountSummaryReportRequest>
{
    public GetHrHeadcountSummaryReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
