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

    // Internal recruitment Ticket 6: restrict to one candidate's applications (the candidate
    // application history on Candidate Detail uses this).
    public Guid? CandidateId { get; init; }

    // Internal recruitment Ticket 6: optional Internal/External filter. null = all; true = only
    // Source == Internal; false = every other application (including legacy null-Source rows and
    // external candidates who were later hired).
    public bool? IsInternal { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
