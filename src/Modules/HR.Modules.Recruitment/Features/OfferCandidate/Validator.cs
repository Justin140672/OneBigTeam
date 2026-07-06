using FluentValidation;

namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed class OfferCandidateValidator : AbstractValidator<OfferCandidateRequest>
{
    public OfferCandidateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.ApplicationId)
            .NotEmpty();
    }
}
