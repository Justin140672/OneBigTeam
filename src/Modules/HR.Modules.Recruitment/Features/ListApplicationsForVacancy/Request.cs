namespace HR.Modules.Recruitment.Features.ListApplicationsForVacancy;

internal sealed record ListApplicationsForVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid? StageId { get; init; }

    // Internal recruitment Ticket 6: optional Internal/External filter (query string ?isInternal=).
    // null = all applications; true = only Source == Internal; false = everything else (including
    // legacy applications with a null Source and external candidates who were later hired).
    public bool? IsInternal { get; init; }
}
