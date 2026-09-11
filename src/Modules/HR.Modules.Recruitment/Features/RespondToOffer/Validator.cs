using FluentValidation;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.RespondToOffer;

internal sealed class RespondToOfferValidator : AbstractValidator<RespondToOfferRequest>
{
    private static readonly OfferResponseStatus[] AllowedTargets =
    {
        OfferResponseStatus.Accepted,
        OfferResponseStatus.Declined,
        OfferResponseStatus.Withdrawn
    };

    public RespondToOfferValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.VacancyId).NotEmpty();
        RuleFor(r => r.ApplicationId).NotEmpty();

        RuleFor(r => r.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<OfferResponseStatus>(s, ignoreCase: true, out var parsed)
                       && AllowedTargets.Contains(parsed))
            .WithMessage("Status must be one of: Accepted, Declined, Withdrawn.");
    }
}
