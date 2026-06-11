using FluentValidation;

namespace HR.Modules.Leave.Features.GetEmployeeLeaveBalance;

internal sealed class GetEmployeeLeaveBalanceValidator : AbstractValidator<GetEmployeeLeaveBalanceRequest>
{
    public GetEmployeeLeaveBalanceValidator()
    {
        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.PolicyYear)
            .InclusiveBetween(2000, 2100);
    }
}
