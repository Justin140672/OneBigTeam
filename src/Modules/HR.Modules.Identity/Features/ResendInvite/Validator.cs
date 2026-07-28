using FluentValidation;

namespace HR.Modules.Identity.Features.ResendInvite;

internal sealed class ResendInviteValidator : AbstractValidator<ResendInviteRequest>
{
    public ResendInviteValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.InviteId).NotEmpty();
    }
}
