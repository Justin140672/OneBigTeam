namespace HR.Modules.Recruitment.Features.GetStaleVacancies;

internal sealed record GetStaleVacanciesRequest
{
    public Guid CompanyId { get; init; }

    public int? StaleAfterDays { get; init; }
}
