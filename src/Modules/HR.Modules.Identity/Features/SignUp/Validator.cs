using FluentValidation;

namespace HR.Modules.Identity.Features.SignUp;

internal sealed class SignUpValidator : AbstractValidator<SignUpRequest>
{
    public SignUpValidator()
    {
        RuleFor(r => r.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.AdminFirstName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.AdminLastName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.AdminEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(r => r.Password).NotEmpty().MinimumLength(8);
    }
}
