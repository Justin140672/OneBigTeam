using FluentValidation;

namespace HR.Modules.Identity.Features.ResetPassword;

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
    {
        RuleFor(r => r.AccessToken).NotEmpty();
        RuleFor(r => r.NewPassword).NotEmpty().MinimumLength(8);
    }
}
