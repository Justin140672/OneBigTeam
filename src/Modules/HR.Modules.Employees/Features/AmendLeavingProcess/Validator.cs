using FluentValidation;

namespace HR.Modules.Employees.Features.AmendLeavingProcess;

internal sealed class AmendLeavingProcessValidator : AbstractValidator<AmendLeavingProcessRequest>
{
    public AmendLeavingProcessValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.LeavingDate)
            .NotEqual(default(DateOnly));

        RuleFor(r => r.LastWorkingDay)
            .NotEqual(default(DateOnly))
            .LessThanOrEqualTo(r => r.LeavingDate)
            .WithMessage("LastWorkingDay must be on or before LeavingDate.");

        RuleFor(r => r.LeavingReason)
            .IsInEnum();
    }
}
