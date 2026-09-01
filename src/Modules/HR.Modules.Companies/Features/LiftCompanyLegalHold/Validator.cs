using FluentValidation;

namespace HR.Modules.Companies.Features.LiftCompanyLegalHold;

internal sealed class LiftCompanyLegalHoldValidator : AbstractValidator<LiftCompanyLegalHoldRequest>
{
    public LiftCompanyLegalHoldValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
