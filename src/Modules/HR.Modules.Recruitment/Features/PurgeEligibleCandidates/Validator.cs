using FluentValidation;

namespace HR.Modules.Recruitment.Features.PurgeEligibleCandidates;

internal sealed class PurgeEligibleCandidatesValidator : AbstractValidator<PurgeEligibleCandidatesRequest>
{
    public PurgeEligibleCandidatesValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
