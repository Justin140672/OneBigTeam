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

        RuleFor(r => r.Status).IsInEnum();

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

        // Ticket 2: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion)
            .RequireLoadedVersion();
    }
}
