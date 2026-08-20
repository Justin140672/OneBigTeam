using FluentValidation;

namespace HR.Modules.Recruitment.Features.DeactivateCandidate;

internal sealed class DeactivateCandidateValidator : AbstractValidator<DeactivateCandidateRequest>
{
    public DeactivateCandidateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.CandidateId)
            .NotEmpty();

        RuleFor(r => r.Reason)
            .NotEmpty()
            .WithMessage("A reason is required to deactivate a candidate.")
            .MaximumLength(1000);
    }
}
