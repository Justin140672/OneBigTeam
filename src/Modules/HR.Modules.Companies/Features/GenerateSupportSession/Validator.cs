using FluentValidation;

namespace HR.Modules.Companies.Features.GenerateSupportSession;

internal sealed class GenerateSupportSessionValidator : AbstractValidator<GenerateSupportSessionRequest>
{
    public GenerateSupportSessionValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(1000);
    }
}
