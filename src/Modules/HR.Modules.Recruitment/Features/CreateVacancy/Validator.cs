using FluentValidation;

namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed class CreateVacancyValidator : AbstractValidator<CreateVacancyRequest>
{
    public CreateVacancyValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.PositionProfileId)
            .NotEmpty();

        RuleFor(r => r.AdvertTitle)
            .MaximumLength(200)
            .When(r => !string.IsNullOrWhiteSpace(r.AdvertTitle));

        RuleFor(r => r.AdvertDescription)
            .MaximumLength(4000)
            .When(r => !string.IsNullOrWhiteSpace(r.AdvertDescription));

        RuleFor(r => r.HiringManagerId)
            .NotEmpty();
    }
}
