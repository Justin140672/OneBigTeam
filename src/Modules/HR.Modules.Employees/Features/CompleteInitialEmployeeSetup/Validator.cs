using FluentValidation;

namespace HR.Modules.Employees.Features.CompleteInitialEmployeeSetup;

internal sealed class CompleteInitialEmployeeSetupValidator : AbstractValidator<CompleteInitialEmployeeSetupRequest>
{
    public CompleteInitialEmployeeSetupValidator()
    {
        RuleFor(r => r.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.DateOfBirth)
            .NotEmpty()
            .GreaterThan(new DateOnly(1900, 1, 1))
            .WithMessage("Date of birth must be after 1 January 1900.");

        RuleFor(r => r.Nationality)
            .NotEmpty();

        RuleFor(r => r.Gender)
            .NotEmpty();

        RuleFor(r => r.PersonalEmail)
            .MaximumLength(320)
            .EmailAddress()
            .When(r => !string.IsNullOrWhiteSpace(r.PersonalEmail));

        RuleFor(r => r.AddressLine1)
            .NotEmpty();

        RuleFor(r => r.City)
            .NotEmpty();

        RuleFor(r => r.PostCode)
            .NotEmpty();
    }
}
