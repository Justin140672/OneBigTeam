using FluentValidation;

namespace HR.Modules.Recruitment.Features.CreateCandidateApplication;

internal sealed class CreateCandidateApplicationValidator : AbstractValidator<CreateCandidateApplicationRequest>
{
    public CreateCandidateApplicationValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        // Candidate fields: same rules as CreateCandidate.
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

        // Application fields: same rules as CreateApplication.
        RuleFor(r => r.Notes)
            .MaximumLength(2000)
            .When(r => !string.IsNullOrWhiteSpace(r.Notes));

        // NotEmpty() on Guid? only rejects null (default of Guid? is null), so Guid.Empty is checked explicitly.
        RuleFor(r => r.SourceExternalRecruiterId)
            .Must(id => id.HasValue && id.Value != Guid.Empty)
            .WithMessage("SourceExternalRecruiterId is required when Source is ExternalRecruiter.")
            .When(r => r.Source == Domain.ApplicationSource.ExternalRecruiter);

        RuleFor(r => r.SourceExternalRecruiterId)
            .Empty()
            .WithMessage("SourceExternalRecruiterId must not be supplied unless Source is ExternalRecruiter.")
            .When(r => r.Source != Domain.ApplicationSource.ExternalRecruiter);

        // The CV is optional. Its size/type rules are the configured candidate-document limits, enforced
        // by the shared upload staging rules (identical to UploadCandidateDocument).
    }
}
