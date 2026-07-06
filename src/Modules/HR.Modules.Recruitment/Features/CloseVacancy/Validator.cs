using FluentValidation;

namespace HR.Modules.Recruitment.Features.CloseVacancy;

internal sealed class CloseVacancyValidator : AbstractValidator<CloseVacancyRequest>
{
    public CloseVacancyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();
    }
}
