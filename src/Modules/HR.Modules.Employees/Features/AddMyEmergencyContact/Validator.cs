using FluentValidation;

namespace HR.Modules.Employees.Features.AddMyEmergencyContact;

internal sealed class AddMyEmergencyContactValidator : AbstractValidator<AddMyEmergencyContactRequest>
{
    public AddMyEmergencyContactValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must be 200 characters or fewer.");

        RuleFor(r => r.Relationship)
            .NotEmpty().WithMessage("Relationship is required.")
            .MaximumLength(100).WithMessage("Relationship must be 100 characters or fewer.");

        RuleFor(r => r.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.")
            .MaximumLength(30).WithMessage("Phone number must be 30 characters or fewer.");

        RuleFor(r => r.Email)
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .When(r => !string.IsNullOrWhiteSpace(r.Email));
    }
}
