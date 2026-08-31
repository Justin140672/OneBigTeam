using FluentValidation;

namespace HR.Modules.Employees.Features.GetManagerTeamStatusSummary;

internal sealed class GetManagerTeamStatusSummaryValidator
    : AbstractValidator<GetManagerTeamStatusSummaryRequest>
{
    public GetManagerTeamStatusSummaryValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.ManagerId).NotEmpty();
    }
}
