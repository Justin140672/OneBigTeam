namespace HR.Modules.Recruitment.Features.PublishVacancy;

internal sealed record PublishVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public DateOnly? OpenedAt { get; init; }
}
