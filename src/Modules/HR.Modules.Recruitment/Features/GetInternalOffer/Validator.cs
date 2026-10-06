using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetInternalOffer;

internal sealed class GetInternalOfferValidator : AbstractValidator<GetInternalOfferRequest>
{
    public GetInternalOfferValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.ApplicationId).NotEmpty();
    }
}
