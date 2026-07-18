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

        RuleFor(r => r.PositionProfileId)
            .NotEqual(Guid.Empty)
            .When(r => r.PositionProfileId.HasValue);

        RuleFor(r => r.AdvertTitle)
            .MaximumLength(200)
            .When(r => !string.IsNullOrWhiteSpace(r.AdvertTitle));

        RuleFor(r => r.AdvertDescription)
            .MaximumLength(4000)
            .When(r => !string.IsNullOrWhiteSpace(r.AdvertDescription));

        RuleFor(r => r.HiringManagerId)
            .NotEmpty();

        RuleFor(r => r.CorrectionReason)
            .NotEmpty()
            .WithMessage("A reason is required when requesting an authorised correction.")
            .MaximumLength(1000)
            .When(r => r.IsAuthorisedCorrection);
    }
}
