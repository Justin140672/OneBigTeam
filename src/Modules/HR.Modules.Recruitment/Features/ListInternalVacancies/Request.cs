namespace HR.Modules.Recruitment.Features.ListInternalVacancies;

internal sealed record ListInternalVacanciesRequest
{
    public Guid CompanyId { get; init; }
    public string? Search { get; init; }
}
