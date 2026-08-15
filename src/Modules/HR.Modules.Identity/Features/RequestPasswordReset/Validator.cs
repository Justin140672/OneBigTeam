using FluentValidation;

namespace HR.Modules.Identity.Features.RequestPasswordReset;

internal sealed class RequestPasswordResetValidator : AbstractValidator<RequestPasswordResetRequest>
{
    public RequestPasswordResetValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}
