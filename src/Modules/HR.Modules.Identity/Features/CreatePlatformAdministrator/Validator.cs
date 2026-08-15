using FluentValidation;

namespace HR.Modules.Identity.Features.CreatePlatformAdministrator;

internal sealed class CreatePlatformAdministratorValidator : AbstractValidator<CreatePlatformAdministratorRequest>
{
    public CreatePlatformAdministratorValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
        RuleFor(r => r.Role).IsInEnum();
    }
}
