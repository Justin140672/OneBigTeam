using FluentValidation;

namespace HR.Modules.Companies.Features.RedeemSupportSession;

internal sealed class RedeemSupportSessionValidator : AbstractValidator<RedeemSupportSessionRequest>
{
    public RedeemSupportSessionValidator()
    {
        RuleFor(r => r.Token)
            .NotEmpty();
    }
}
