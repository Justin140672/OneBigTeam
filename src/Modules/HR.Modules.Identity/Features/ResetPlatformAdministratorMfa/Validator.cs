using FluentValidation;

namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

internal sealed class ResetPlatformAdministratorMfaValidator : AbstractValidator<ResetPlatformAdministratorMfaRequest>
{
    public ResetPlatformAdministratorMfaValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
    }
}
