using FluentValidation;

namespace HR.Modules.Recruitment.Features.ApproveVacancy;

internal sealed class ApproveVacancyValidator : AbstractValidator<ApproveVacancyRequest>
{
    public ApproveVacancyValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.VacancyId).NotEmpty();
    }
}
