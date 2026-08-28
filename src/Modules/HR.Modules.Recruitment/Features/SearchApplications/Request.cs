namespace HR.Modules.Recruitment.Features.SearchApplications;

internal sealed record SearchApplicationsRequest
{
    public Guid CompanyId { get; init; }
    public string? Search { get; init; }
    public Guid? VacancyId { get; init; }
    public Guid? StageId { get; init; }
    public Guid? ExternalRecruiterId { get; init; }
    public DateTimeOffset? AppliedFrom { get; init; }
    public DateTimeOffset? AppliedTo { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
