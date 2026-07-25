using FluentValidation;

namespace HR.Modules.Employees.Features.BulkApplyCompensationAdjustments;

internal sealed class BulkApplyCompensationAdjustmentsValidator : AbstractValidator<BulkApplyCompensationAdjustmentsRequest>
{
    public BulkApplyCompensationAdjustmentsValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.EffectiveDate)
            .NotEmpty()
            .WithMessage("EffectiveDate is required.");

        RuleFor(r => r.Reason)
            .IsInEnum();

        RuleFor(r => r.AdjustmentMode)
            .IsInEnum();

        RuleFor(r => r.Notes)
            .MaximumLength(4000)
            .When(r => r.Notes is not null);

        RuleFor(r => r.Items)
            .NotEmpty()
            .WithMessage("At least one employee must be selected.");

        RuleFor(r => r.Items)
            .Must(items => items.Select(i => i.EmployeeId).Distinct().Count() == items.Count)
            .WithMessage("Each employee can only appear once in a single bulk adjustment.")
            .When(r => r.Items.Count > 0);

        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.EmployeeId)
                .NotEmpty();

            item.RuleFor(i => i.ProposedSalary)
                .GreaterThan(0);

            item.RuleFor(i => i.SalaryType)
                .IsInEnum();

            item.RuleFor(i => i.Currency)
                .NotEmpty()
                .Length(3)
                .WithMessage("Currency must be a 3-letter ISO 4217 code (e.g. GBP).");

            item.RuleFor(i => i.HoursPerWeek)
                .GreaterThan(0)
                .When(i => i.HoursPerWeek.HasValue);

            item.RuleFor(i => i.FTE)
                .InclusiveBetween(0, 1)
                .When(i => i.FTE.HasValue);
        });
    }
}
