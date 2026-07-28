using FluentValidation;

namespace HR.Modules.Identity.Features.CancelInvite;

internal sealed class CancelInviteValidator : AbstractValidator<CancelInviteRequest>
{
    public CancelInviteValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.InviteId).NotEmpty();
    }
}
