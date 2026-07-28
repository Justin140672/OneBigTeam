using FluentValidation;

namespace HR.Modules.Identity.Features.DisableUser;

internal sealed class DisableUserValidator : AbstractValidator<DisableUserRequest>
{
    public DisableUserValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.UserId).NotEmpty();
    }
}
