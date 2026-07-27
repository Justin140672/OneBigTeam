namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

internal sealed record GetRecruitmentKanbanRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
}
