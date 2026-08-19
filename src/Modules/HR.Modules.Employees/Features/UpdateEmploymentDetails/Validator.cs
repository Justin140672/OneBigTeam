using FluentValidation;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Employees.Features.UpdateEmploymentDetails;

internal sealed class UpdateEmploymentDetailsValidator : AbstractValidator<UpdateEmploymentDetailsRequest>
{
    public UpdateEmploymentDetailsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.StartDate).NotEmpty();

        // Draft is deliberately not one of the selectable options on the Employment tab's status
        // dropdown (see EmployeeEmploymentTab.razor's _statusOptions) — it's a new-employee-only
        // starting state, not something a user picks. But a request-shape rule can't tell "this
        // employee is still Draft and this edit doesn't touch status" apart from "someone is
        // actively reverting an Active employee back to Draft" — it only sees the submitted value,
        // not the employee's current one. Rejecting Status == Draft outright therefore also
        // rejected every legitimate edit (e.g. assigning a manager) made before a brand-new
        // employee's first Activate, since the form round-trips their still-Draft status
        // unchanged. That check now lives in the handler, which has the employee's current status
        // to compare against and can distinguish an actual attempted transition into Draft from a
        // Draft employee simply staying Draft.

        // Same format rule Wave 1 applied to CreateEmployee — an employee number changed here is a
        // genuine administrative correction, so it must satisfy the same constraints a brand-new
        // number would (uniqueness is enforced separately in the handler, which has DB access).
        RuleFor(r => r.EmployeeNumber)
            .NotEmpty().WithMessage("Employee number is required.")
            .MaximumLength(50)
            .Matches(CreateEmployeeValidator.EmployeeNumberPattern)
                .WithMessage("Employee number may only contain letters, numbers, spaces, and the separators - _ . /");

        RuleFor(r => r.EmploymentTypeId)
            .NotEqual(Guid.Empty).When(r => r.EmploymentTypeId.HasValue);

        RuleFor(r => r.Notes)
            .MaximumLength(4000)
            .When(r => !string.IsNullOrWhiteSpace(r.Notes));

        RuleFor(r => r.HoursPerDayOverride)
            .GreaterThan(0).LessThanOrEqualTo(24)
            .When(r => r.HoursPerDayOverride.HasValue);

        RuleFor(r => r.WorkingDaysOverride)
            .NotEqual(WorkingDays.None)
            .When(r => r.WorkingDaysOverride.HasValue);

        RuleFor(r => r)
            .Must(r => r.NoticePeriodUnitOverride.HasValue == r.NoticePeriodLengthOverride.HasValue)
            .WithMessage("Notice period unit and length overrides must both be set or both be empty.");

        RuleFor(r => r.NoticePeriodLengthOverride)
            .GreaterThan(0)
            .When(r => r.NoticePeriodLengthOverride.HasValue)
            .WithMessage("NoticePeriodLengthOverride must be greater than 0.");
    }
}
