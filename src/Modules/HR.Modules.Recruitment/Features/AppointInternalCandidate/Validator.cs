using FluentValidation;

namespace HR.Modules.Recruitment.Features.AppointInternalCandidate;

internal sealed class AppointInternalCandidateValidator : AbstractValidator<AppointInternalCandidateRequest>
{
    private static readonly string[] SalaryTypes = ["Annual", "Hourly", "Daily"];

    public AppointInternalCandidateValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.VacancyId).NotEmpty();
        RuleFor(r => r.ApplicationId).NotEmpty();

        RuleFor(r => r.EffectiveDate)
            .Must(d => d!.Value != default)
            .When(r => r.EffectiveDate.HasValue)
            .WithMessage("Effective date is invalid.");

        RuleFor(r => r.ManagerId)
            .Must(id => id is { } managerId && managerId != Guid.Empty)
            .When(r => !r.NoManager)
            .WithMessage("Select a manager, or choose 'No manager'.");

        RuleFor(r => r.ManagerId)
            .Null()
            .When(r => r.NoManager)
            .WithMessage("A manager cannot be selected together with 'No manager'.");

        When(r => r.CreateCompensationChange, () =>
        {
            RuleFor(r => r.CompensationSalaryType)
                .NotEmpty()
                .Must(t => SalaryTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
                .WithMessage("Salary type must be Annual, Hourly or Daily.");

            RuleFor(r => r.CompensationSalary)
                .NotNull()
                .GreaterThan(0);

            RuleFor(r => r.CompensationCurrency)
                .NotEmpty()
                .Length(3)
                .WithMessage("Currency must be a 3-letter ISO 4217 code (e.g. GBP).");

            RuleFor(r => r.CompensationHoursPerWeek)
                .GreaterThan(0)
                .When(r => r.CompensationHoursPerWeek.HasValue);

            RuleFor(r => r.CompensationFte)
                .InclusiveBetween(0, 1)
                .When(r => r.CompensationFte.HasValue);

            RuleFor(r => r.CompensationNotes)
                .MaximumLength(4000);
        });
    }
}
