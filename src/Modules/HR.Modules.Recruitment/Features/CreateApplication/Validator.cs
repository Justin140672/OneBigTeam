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
    }
}
