using FluentValidation;

namespace HR.Modules.Recruitment.Features.CreateCandidate;

internal sealed class CreateCandidateValidator : AbstractValidator<CreateCandidateRequest>
{
    public CreateCandidateValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();

        RuleFor(r => r.Phone)
            .MaximumLength(30)
            .When(r => !string.IsNullOrWhiteSpace(r.Phone));

        RuleFor(r => r.ResumeUrl)
            .MaximumLength(500)
            .When(r => !string.IsNullOrWhiteSpace(r.ResumeUrl));
    }
}
