using FluentValidation;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.CreateEmployee;

internal sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeRequest>
{
    // Letters, numbers, spaces, and a documented set of common separators. Kept permissive
    // enough that legitimate real-world employee number schemes aren't rejected, while still
    // excluding characters that would be awkward in exports/URLs/reports.
    public const string EmployeeNumberPattern = @"^[A-Za-z0-9 \-_./]+$";

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

        // NotEmpty is intentionally not enforced here: in Automatic employee-numbering mode the
        // request may omit EmployeeNumber entirely and the handler generates one. Requiredness in
        // Manual mode is enforced by the handler instead, since it depends on the company's
        // CompanySettings.EmployeeNumberMode (a DB read the validator does not perform).
        RuleFor(r => r.EmployeeNumber)
            .MaximumLength(50)
            .Matches(EmployeeNumberPattern)
                .WithMessage("Employee number may only contain letters, numbers, spaces, and the separators - _ . /")
            .When(r => !string.IsNullOrWhiteSpace(r.EmployeeNumber));

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

        RuleFor(r => r.AddressLine1)
            .NotEmpty().WithMessage("Address line 1 is required.")
            .MaximumLength(200);

        RuleFor(r => r.AddressLine2).MaximumLength(200);

        RuleFor(r => r.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(100);

        RuleFor(r => r.County).MaximumLength(100);

        RuleFor(r => r.PostCode)
            .NotEmpty().WithMessage("Postcode is required.")
            .MaximumLength(20);

        RuleFor(r => r.Salary)
            .NotNull().WithMessage("Salary is required.")
            .GreaterThan(0m).WithMessage("Salary must be greater than 0.");

        RuleFor(r => r.SalaryFrequency)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Salary type is required.")
            .Must(value => Enum.TryParse<SalaryType>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                .WithMessage("Salary type must be Annual, Hourly or Daily.");

        RuleFor(r => r.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a 3-letter ISO 4217 code (e.g. GBP).");
    }
}
