using FluentValidation;

namespace HR.Modules.Identity.Features.DisablePlatformAdministrator;

internal sealed class DisablePlatformAdministratorValidator : AbstractValidator<DisablePlatformAdministratorRequest>
{
    public DisablePlatformAdministratorValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
    }
}
