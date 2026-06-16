using FluentValidation;

namespace HR.Modules.Employees.Features.UpdateMyContactDetails;

internal sealed class UpdateMyContactDetailsValidator : AbstractValidator<UpdateMyContactDetailsRequest>
{
    public UpdateMyContactDetailsValidator()
    {
        RuleFor(r => r.PersonalEmail)
            .EmailAddress().WithMessage("Personal email must be a valid email address.")
            .When(r => !string.IsNullOrWhiteSpace(r.PersonalEmail));

        RuleFor(r => r.PhoneNumber)
            .MaximumLength(30).WithMessage("Phone number must be 30 characters or fewer.");

        RuleFor(r => r.HomePhone)
            .MaximumLength(30).WithMessage("Home phone must be 30 characters or fewer.");

        RuleFor(r => r.AddressLine1)
            .NotEmpty().WithMessage("Address line 1 is required.");

        RuleFor(r => r.City)
            .NotEmpty().WithMessage("City is required.");

        RuleFor(r => r.PostCode)
            .NotEmpty().WithMessage("Post code is required.");

        RuleFor(r => r.Country)
            .NotEmpty().WithMessage("Country is required.");
    }
}
