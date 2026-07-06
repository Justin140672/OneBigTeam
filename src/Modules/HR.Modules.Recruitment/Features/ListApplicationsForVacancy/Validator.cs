using FluentValidation;

namespace HR.Modules.Recruitment.Features.ListApplicationsForVacancy;

internal sealed class ListApplicationsForVacancyValidator : AbstractValidator<ListApplicationsForVacancyRequest>
{
    public ListApplicationsForVacancyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();
    }
}
