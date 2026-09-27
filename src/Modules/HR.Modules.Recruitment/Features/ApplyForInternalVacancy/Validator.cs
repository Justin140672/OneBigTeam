using FluentValidation;

namespace HR.Modules.Recruitment.Features.ApplyForInternalVacancy;

internal sealed class ApplyForInternalVacancyValidator : AbstractValidator<ApplyForInternalVacancyRequest>
{
    public ApplyForInternalVacancyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        // Size/extension/content-type rules are the configured candidate-document limits, enforced by
        // the handler through the shared CandidateDocumentUploadStaging rules.
        RuleFor(r => r.CvFile)
            .NotNull()
            .WithMessage("A CV file is required to apply.");
    }
}
