using FluentValidation;

namespace HR.Modules.Recruitment.Features.AddInterviewStage;

internal sealed class AddInterviewStageValidator : AbstractValidator<AddInterviewStageRequest>
{
    public AddInterviewStageValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.Name)
            .MaximumLength(100)
            .Must(n => n is null || n.Trim().Length > 0)
            .WithMessage("Name cannot be blank.");
    }
}
