using FluentValidation;

namespace HR.Modules.Recruitment.Features.ReorderRecruitmentStages;

internal sealed class ReorderRecruitmentStagesValidator : AbstractValidator<ReorderRecruitmentStagesRequest>
{
    public ReorderRecruitmentStagesValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.OrderedStageIds)
            .NotEmpty();

        RuleFor(r => r.OrderedStageIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("The ordered stage id list must not contain duplicates.")
            .When(r => r.OrderedStageIds is { Count: > 0 });
    }
}
