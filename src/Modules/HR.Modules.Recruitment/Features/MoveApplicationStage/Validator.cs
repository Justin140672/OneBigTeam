using FluentValidation;
using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.MoveApplicationStage;

internal sealed class MoveApplicationStageValidator : AbstractValidator<MoveApplicationStageRequest>
{
    public MoveApplicationStageValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();

        RuleFor(r => r.ApplicationId)
            .NotEmpty();

        RuleFor(r => r.NewStatus)
            .IsInEnum();

        RuleFor(r => r.Notes)
            .MaximumLength(2000)
            .When(r => !string.IsNullOrWhiteSpace(r.Notes));
    }
}
