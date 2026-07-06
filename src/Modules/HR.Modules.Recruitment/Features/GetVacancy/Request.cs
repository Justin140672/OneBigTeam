namespace HR.Modules.Recruitment.Features.GetVacancy;

internal sealed record GetVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
}
