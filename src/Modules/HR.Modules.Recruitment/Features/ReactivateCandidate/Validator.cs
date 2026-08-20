using FluentValidation;

namespace HR.Modules.Recruitment.Features.ReactivateCandidate;

internal sealed class ReactivateCandidateValidator : AbstractValidator<ReactivateCandidateRequest>
{
    public ReactivateCandidateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.CandidateId)
            .NotEmpty();
    }
}
