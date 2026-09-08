using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetInternalVacancy;

internal sealed class GetInternalVacancyValidator : AbstractValidator<GetInternalVacancyRequest>
{
    public GetInternalVacancyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();
    }
}
