using FluentValidation;

namespace HR.Modules.Companies.Features.PlaceCompanyLegalHold;

internal sealed class PlaceCompanyLegalHoldValidator : AbstractValidator<PlaceCompanyLegalHoldRequest>
{
    public PlaceCompanyLegalHoldValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .MinimumLength(5)
            .MaximumLength(1000);
    }
}
