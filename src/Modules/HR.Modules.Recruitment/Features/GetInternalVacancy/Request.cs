namespace HR.Modules.Recruitment.Features.GetInternalVacancy;

internal sealed record GetInternalVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
}
