using FluentValidation;

namespace HR.Modules.Identity.Features.EnableUser;

internal sealed class EnableUserValidator : AbstractValidator<EnableUserRequest>
{
    public EnableUserValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.UserId).NotEmpty();
    }
}
