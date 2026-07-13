namespace HR.Modules.Recruitment.Features.GetStaleVacancies;

internal sealed record GetStaleVacanciesRequest
{
    public Guid CompanyId { get; init; }

    // Vacancies with no application activity in at least this many days are considered stale.
    // Optional query parameter — defaults to 14 in the handler when not supplied.
    public int? StaleAfterDays { get; init; }
}
