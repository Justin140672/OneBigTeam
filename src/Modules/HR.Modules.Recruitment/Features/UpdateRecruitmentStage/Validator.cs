using FluentValidation;

namespace HR.Modules.Recruitment.Features.UpdateRecruitmentStage;

internal sealed class UpdateRecruitmentStageValidator : AbstractValidator<UpdateRecruitmentStageRequest>
{
    public UpdateRecruitmentStageValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.RecruitmentStageId).NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.TerminalOutcome)
            .IsInEnum();
    }
}
