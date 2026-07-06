namespace HR.Modules.Recruitment.Features.ListInterviewsForVacancy;

internal sealed record ListInterviewsForVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
}
