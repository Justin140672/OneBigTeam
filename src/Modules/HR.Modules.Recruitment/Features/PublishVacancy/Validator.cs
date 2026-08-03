using FluentValidation;

namespace HR.Modules.Recruitment.Features.PublishVacancy;

internal sealed class PublishVacancyValidator : AbstractValidator<PublishVacancyRequest>
{
    public PublishVacancyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();
    }
}
