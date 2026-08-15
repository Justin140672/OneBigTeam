using FluentValidation;

namespace HR.Modules.Identity.Features.ResetPlatformAdministratorPassword;

internal sealed class ResetPlatformAdministratorPasswordValidator : AbstractValidator<ResetPlatformAdministratorPasswordRequest>
{
    public ResetPlatformAdministratorPasswordValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
    }
}
