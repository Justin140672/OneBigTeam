using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed record ListVacanciesRequest
{
    public Guid CompanyId { get; init; }
    public VacancyStatus? Status { get; init; }

    // Direct filter — matches Vacancy.PositionProfileId exactly.
    public Guid? PositionProfileId { get; init; }

    // Indirect filter — Vacancy has no department column of its own (department comes exclusively
    // from the linked Position Profile), so the handler first resolves the set of Position Profile IDs
    // belonging to this department (via IPositionProfileReader.GetIdsByDepartmentAsync) and then
    // filters vacancies whose PositionProfileId is in that set.
    public Guid? DepartmentId { get; init; }
}
