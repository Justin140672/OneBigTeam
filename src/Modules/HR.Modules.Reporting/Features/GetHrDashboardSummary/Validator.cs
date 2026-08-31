using FluentValidation;

namespace HR.Modules.Reporting.Features.GetHrDashboardSummary;

internal sealed class GetHrDashboardSummaryValidator : AbstractValidator<GetHrDashboardSummaryRequest>
{
    public GetHrDashboardSummaryValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
