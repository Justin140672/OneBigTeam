using FluentValidation;

namespace HR.Modules.Companies.Features.RevokeSupportSession;

internal sealed class RevokeSupportSessionValidator : AbstractValidator<RevokeSupportSessionRequest>
{
    public RevokeSupportSessionValidator()
    {
        RuleFor(r => r.SupportSessionId)
            .NotEmpty();
    }
}
