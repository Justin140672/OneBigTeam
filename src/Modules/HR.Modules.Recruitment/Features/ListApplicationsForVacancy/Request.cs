using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListApplicationsForVacancy;

internal sealed record ListApplicationsForVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public ApplicationStatus? Status { get; init; }
}
