using FluentValidation;

namespace HR.Modules.Recruitment.Features.RespondToInternalOffer;

internal sealed class RespondToInternalOfferValidator : AbstractValidator<RespondToInternalOfferRequest>
{
    public RespondToInternalOfferValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ApplicationId).NotEmpty();

        RuleFor(r => r.Decision)
            .NotEmpty()
            .Must(d => string.Equals(d, "Accept", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(d, "Decline", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Decision must be 'Accept' or 'Decline'.");

        RuleFor(r => r.OfferVersion).GreaterThan(0);

        RuleFor(r => r.Reason).MaximumLength(1000);
    }
}
