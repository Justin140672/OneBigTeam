using FluentValidation;

namespace HR.Modules.Reporting.Features.GetManagerDashboardSummary;

internal sealed class GetManagerDashboardSummaryValidator : AbstractValidator<GetManagerDashboardSummaryRequest>
{
    public GetManagerDashboardSummaryValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
