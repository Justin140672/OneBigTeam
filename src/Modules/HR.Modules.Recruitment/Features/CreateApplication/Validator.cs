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

        // Internal recruitment Ticket 4: Internal is recorded only by the employee Apply endpoint, for
        // an employee-linked candidate — a recruiter cannot assign it.
        RuleFor(r => r.Source)
            .NotEqual(Domain.ApplicationSource.Internal)
            .WithMessage("Source 'Internal' is recorded automatically when an employee applies for an internal vacancy and cannot be set manually.");

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
