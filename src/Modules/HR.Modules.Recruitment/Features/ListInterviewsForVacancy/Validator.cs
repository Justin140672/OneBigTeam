using FluentValidation;

namespace HR.Modules.Recruitment.Features.ListInterviewsForVacancy;

internal sealed class ListInterviewsForVacancyValidator : AbstractValidator<ListInterviewsForVacancyRequest>
{
    public ListInterviewsForVacancyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();
    }
}
