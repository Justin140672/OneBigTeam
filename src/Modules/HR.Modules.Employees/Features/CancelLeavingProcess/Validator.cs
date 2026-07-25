using FluentValidation;

namespace HR.Modules.Employees.Features.CancelLeavingProcess;

internal sealed class CancelLeavingProcessValidator : AbstractValidator<CancelLeavingProcessRequest>
{
    public CancelLeavingProcessValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.CancellationReason)
            .NotEmpty()
            .MaximumLength(1000);
    }
}
