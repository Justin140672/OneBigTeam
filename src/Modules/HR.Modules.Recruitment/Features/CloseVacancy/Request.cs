namespace HR.Modules.Recruitment.Features.CloseVacancy;

internal sealed record CloseVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public DateOnly? ClosedAt { get; init; }
}
