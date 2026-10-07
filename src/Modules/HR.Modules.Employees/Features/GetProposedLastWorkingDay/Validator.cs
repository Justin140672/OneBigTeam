using FluentValidation;

namespace HR.Modules.Employees.Features.GetProposedLastWorkingDay;

internal sealed class GetProposedLastWorkingDayValidator : AbstractValidator<GetProposedLastWorkingDayRequest>
{
    public GetProposedLastWorkingDayValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.LeavingDate)
            .NotEqual(default(DateOnly));
    }
}
