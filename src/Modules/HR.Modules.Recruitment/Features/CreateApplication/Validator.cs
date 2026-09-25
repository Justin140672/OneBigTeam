using FluentValidation;

namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed class CreateApplicationValidator : AbstractValidator<CreateApplicationRequest>
{
    public CreateApplicationValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.CandidateId)
            .NotEmpty();

        RuleFor(r => r.Notes)
            .MaximumLength(2000)
            .When(r => !string.IsNullOrWhiteSpace(r.Notes));

        // Ticket #78: source and recruiter reference are validated as a pair.
        RuleFor(r => r.SourceExternalRecruiterId)
            .NotEmpty()
            .WithMessage("SourceExternalRecruiterId is required when Source is ExternalRecruiter.")
            .When(r => r.Source == Domain.ApplicationSource.ExternalRecruiter);

        RuleFor(r => r.SourceExternalRecruiterId)
            .Empty()
            .WithMessage("SourceExternalRecruiterId must not be supplied unless Source is ExternalRecruiter.")
            .When(r => r.Source != Domain.ApplicationSource.ExternalRecruiter);

        // Internal recruitment Ticket 1: optional, but an explicit empty GUID is a malformed request.
        RuleFor(r => r.CvDocumentId)
            .NotEqual(Guid.Empty)
            .WithMessage("CvDocumentId must not be an empty identifier.")
            .When(r => r.CvDocumentId is not null);
    }
}
