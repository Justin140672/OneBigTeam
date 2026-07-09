using FluentValidation;

namespace HR.Modules.Leave.Features.AdjustLeaveBalance;

internal sealed class AdjustLeaveBalanceValidator : AbstractValidator<AdjustLeaveBalanceRequest>
{
    public AdjustLeaveBalanceValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.LeaveTypeId).NotEmpty();
        RuleFor(r => r.AdjustmentHours)
            .NotEqual(0m).WithMessage("Adjustment cannot be zero.")
            .InclusiveBetween(-9999.99m, 9999.99m).WithMessage("Adjustment must be between -9,999.99 and 9,999.99 hours.");
        RuleFor(r => r.Reason).IsInEnum();
        RuleFor(r => r.Comments).MaximumLength(500).When(r => r.Comments is not null);
    }
}
