namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed record CreateVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
    public string? AdvertTitle { get; init; }
    public string? AdvertDescription { get; init; }
    public Guid HiringManagerId { get; init; }
}
