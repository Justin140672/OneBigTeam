namespace HR.Modules.Recruitment.Features.UpdateVacancy;

internal sealed record UpdateVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Location { get; init; }
    public Guid HiringManagerId { get; init; }
}
