using FluentValidation;

namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed class CreateVacancyValidator : AbstractValidator<CreateVacancyRequest>
{
    public CreateVacancyValidator()
    {
        RuleFor(r => r.CompanyId)
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
