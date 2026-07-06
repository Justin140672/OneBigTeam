using FluentValidation;

namespace HR.Modules.Recruitment.Features.HireCandidate;

internal sealed class HireCandidateValidator : AbstractValidator<HireCandidateRequest>
{
    public HireCandidateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.ApplicationId)
            .NotEmpty();
    }
}
