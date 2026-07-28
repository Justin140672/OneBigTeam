using FluentValidation;

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

        RuleFor(r => r.NewStageId)
            .NotEmpty();

        RuleFor(r => r.Notes)
            .MaximumLength(2000)
            .When(r => !string.IsNullOrWhiteSpace(r.Notes));
    }
}
