using FluentValidation;

namespace HR.Modules.Employees.Features.PromoteEmployee;

internal sealed class PromoteEmployeeValidator : AbstractValidator<PromoteEmployeeRequest>
{
    public PromoteEmployeeValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.NewPositionProfileId)
            .NotEmpty();

        RuleFor(r => r.EffectiveDate)
            .NotEqual(default(DateOnly));

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(r => r.CompensationSalaryType)
            .NotNull()
            .When(r => r.CreateCompensationChange);

        RuleFor(r => r.CompensationSalary)
            .NotNull()
            .GreaterThan(0)
            .When(r => r.CreateCompensationChange);

        RuleFor(r => r.CompensationCurrency)
            .NotEmpty()
            .When(r => r.CreateCompensationChange);
    }
}
