namespace HR.Modules.Recruitment.Features.ApproveVacancy;

internal sealed record ApproveVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
}
