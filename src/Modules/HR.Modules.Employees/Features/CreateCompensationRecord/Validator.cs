using FluentValidation;

namespace HR.Modules.Employees.Features.CreateCompensationRecord;

internal sealed class CreateCompensationRecordValidator : AbstractValidator<CreateCompensationRecordRequest>
{
    public CreateCompensationRecordValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EmployeeId)
            .NotEmpty();

        RuleFor(r => r.EffectiveFrom)
            .NotEmpty()
            .WithMessage("EffectiveFrom is required.");

        RuleFor(r => r.SalaryType)
            .IsInEnum();

        RuleFor(r => r.Reason)
            .IsInEnum();

        RuleFor(r => r.Salary)
            .GreaterThan(0);

        RuleFor(r => r.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a 3-letter ISO 4217 code (e.g. GBP).");

        RuleFor(r => r.HoursPerWeek)
            .GreaterThan(0)
            .When(r => r.HoursPerWeek.HasValue);

        RuleFor(r => r.FTE)
            .InclusiveBetween(0, 1)
            .When(r => r.FTE.HasValue);

        RuleFor(r => r.Notes)
            .MaximumLength(4000)
            .When(r => r.Notes is not null);
    }
}
