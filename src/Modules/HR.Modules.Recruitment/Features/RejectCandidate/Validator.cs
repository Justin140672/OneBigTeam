using FluentValidation;

namespace HR.Modules.Recruitment.Features.RejectCandidate;

internal sealed class RejectCandidateValidator : AbstractValidator<RejectCandidateRequest>
{
    public RejectCandidateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.ApplicationId)
            .NotEmpty();

        RuleFor(r => r.RejectionReason)
            .MaximumLength(2000)
            .When(r => !string.IsNullOrWhiteSpace(r.RejectionReason));
    }
}
