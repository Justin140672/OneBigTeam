namespace HR.Modules.Recruitment.Features.RejectCandidate;

internal sealed record RejectCandidateRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }
    public string? RejectionReason { get; init; }
}
