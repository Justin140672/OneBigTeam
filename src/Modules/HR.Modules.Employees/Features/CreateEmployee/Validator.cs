using FluentValidation;

namespace HR.Modules.Employees.Features.CreateEmployee;

internal sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.DepartmentId)
            .NotEmpty().WithMessage("Department is required.");

        RuleFor(r => r.LocationId)
            .NotEmpty().WithMessage("Location is required.");

        RuleFor(r => r.PositionProfileId)
            .NotEmpty().WithMessage("Position profile is required.");

        RuleFor(r => r.EmploymentTypeId)
            .NotEmpty().WithMessage("Employment type is required.");

        RuleFor(r => r.EmployeeNumber)
            .NotEmpty().WithMessage("Employee number is required.")
            .MaximumLength(50);

        RuleFor(r => r.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.PreferredName)
            .MaximumLength(100)
            .When(r => !string.IsNullOrWhiteSpace(r.PreferredName));

        RuleFor(r => r.WorkEmail)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress();

        RuleFor(r => r.PersonalEmail)
            .MaximumLength(320)
            .EmailAddress()
            .When(r => !string.IsNullOrWhiteSpace(r.PersonalEmail));

        RuleFor(r => r.StartDate)
            .NotEmpty();

        RuleFor(r => r.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required.");

        RuleFor(r => r.Nationality)
            .NotEmpty().WithMessage("Nationality is required.")
            .MaximumLength(100);

        RuleFor(r => r.Gender)
            .NotEmpty().WithMessage("Gender is required.")
            .MaximumLength(50);
    }
}
