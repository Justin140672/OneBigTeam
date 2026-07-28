using FluentValidation;

namespace HR.Modules.Recruitment.Features.SetRecruitmentStageActiveStatus;

internal sealed class SetRecruitmentStageActiveStatusValidator : AbstractValidator<SetRecruitmentStageActiveStatusRequest>
{
    public SetRecruitmentStageActiveStatusValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.RecruitmentStageId).NotEmpty();
    }
}
