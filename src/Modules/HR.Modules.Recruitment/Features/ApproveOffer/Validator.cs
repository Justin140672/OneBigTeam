using FluentValidation;

namespace HR.Modules.Recruitment.Features.ApproveOffer;

internal sealed class ApproveOfferValidator : AbstractValidator<ApproveOfferRequest>
{
    public ApproveOfferValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.VacancyId).NotEmpty();
        RuleFor(r => r.ApplicationId).NotEmpty();
    }
}
