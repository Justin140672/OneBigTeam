using FluentValidation;

namespace HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;

internal sealed class ResetPlatformAdministratorMfaValidator : AbstractValidator<ResetPlatformAdministratorMfaRequest>
{
    public ResetPlatformAdministratorMfaValidator()
    {
        RuleFor(r => r.Id).NotEmpty();

        RuleFor(r => r.Confirmed)
            .Equal(true)
            .WithMessage("The MFA reset must be explicitly confirmed.");

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(500)
            .WithMessage("An administrative reason of between 5 and 500 characters is required.");
    }
}
