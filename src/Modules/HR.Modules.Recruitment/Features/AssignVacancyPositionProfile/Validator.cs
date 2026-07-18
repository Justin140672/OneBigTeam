using FluentValidation;

namespace HR.Modules.Recruitment.Features.AssignVacancyPositionProfile;

internal sealed class AssignVacancyPositionProfileValidator : AbstractValidator<AssignVacancyPositionProfileRequest>
{
    public AssignVacancyPositionProfileValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.PositionProfileId)
            .NotEmpty();
    }
}
