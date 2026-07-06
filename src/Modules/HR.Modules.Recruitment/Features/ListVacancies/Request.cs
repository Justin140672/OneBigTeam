using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListVacancies;

internal sealed record ListVacanciesRequest
{
    public Guid CompanyId { get; init; }
    public VacancyStatus? Status { get; init; }
    public Guid? DepartmentId { get; init; }
}
