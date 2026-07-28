namespace HR.Modules.Recruitment.Features.ListApplicationsForVacancy;

internal sealed record ListApplicationsForVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid? StageId { get; init; }
}
