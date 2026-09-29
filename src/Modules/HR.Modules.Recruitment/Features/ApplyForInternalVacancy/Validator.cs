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

        RuleFor(r => r.CvFile)
            .NotNull()
            .WithMessage("A CV file is required to apply.");
    }
}
