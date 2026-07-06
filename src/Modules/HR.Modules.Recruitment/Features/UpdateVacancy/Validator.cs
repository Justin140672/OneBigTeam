using FluentValidation;

namespace HR.Modules.Recruitment.Features.UpdateVacancy;

internal sealed class UpdateVacancyValidator : AbstractValidator<UpdateVacancyRequest>
{
    public UpdateVacancyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.Title)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Description)
            .MaximumLength(4000)
            .When(r => !string.IsNullOrWhiteSpace(r.Description));

        RuleFor(r => r.Location)
            .MaximumLength(200)
            .When(r => !string.IsNullOrWhiteSpace(r.Location));

        RuleFor(r => r.HiringManagerId)
            .NotEmpty();
    }
}
