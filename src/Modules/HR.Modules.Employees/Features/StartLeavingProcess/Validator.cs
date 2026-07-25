using FluentValidation;

namespace HR.Modules.Employees.Features.StartLeavingProcess;

internal sealed class StartLeavingProcessValidator : AbstractValidator<StartLeavingProcessRequest>
{
    public StartLeavingProcessValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.ResignationReceivedDate)
            .NotEqual(default(DateOnly));

        RuleFor(r => r.LeavingDate)
            .NotEqual(default(DateOnly))
            .GreaterThanOrEqualTo(r => r.ResignationReceivedDate)
            .WithMessage("LeavingDate must be on or after ResignationReceivedDate.");

        RuleFor(r => r.LastWorkingDay)
            .NotEqual(default(DateOnly))
            .LessThanOrEqualTo(r => r.LeavingDate)
            .WithMessage("LastWorkingDay must be on or before LeavingDate.");

        RuleFor(r => r.LeavingReason)
            .IsInEnum();
    }
}
