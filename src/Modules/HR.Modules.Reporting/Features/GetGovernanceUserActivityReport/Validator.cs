using FluentValidation;
using HR.Modules.Reporting.GovernanceReporting;

namespace HR.Modules.Reporting.Features.GetGovernanceUserActivityReport;

internal sealed class GetGovernanceUserActivityReportValidator
    : AbstractValidator<GetGovernanceUserActivityReportRequest>
{
    public GetGovernanceUserActivityReportValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.Status)
            .Must(GovernanceReportFilters.IsValidStatus)
            .WithMessage("Status must be either 'Success' or 'Failed'.");

        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate!.Value)
            .When(x => x.FromDate is not null && x.ToDate is not null)
            .WithMessage("ToDate must be on or after FromDate.");
    }
}
