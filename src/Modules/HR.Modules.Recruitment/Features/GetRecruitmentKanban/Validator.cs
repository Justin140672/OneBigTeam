using FluentValidation;

namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

internal sealed class GetRecruitmentKanbanValidator : AbstractValidator<GetRecruitmentKanbanRequest>
{
    public GetRecruitmentKanbanValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.VacancyId)
            .NotEmpty();
    }
}
