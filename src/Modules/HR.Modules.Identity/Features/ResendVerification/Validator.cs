using FluentValidation;

namespace HR.Modules.Identity.Features.ResendVerification;

internal sealed class ResendVerificationValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}
