namespace HR.Modules.Recruitment.Features.CreateApplication;

internal sealed record CreateApplicationRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid CandidateId { get; init; }
    public string? Notes { get; init; }
}
