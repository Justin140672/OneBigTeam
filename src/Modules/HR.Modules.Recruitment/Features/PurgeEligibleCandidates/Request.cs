namespace HR.Modules.Recruitment.Features.PurgeEligibleCandidates;

internal sealed record PurgeEligibleCandidatesRequest
{
    public Guid CompanyId { get; init; }
}
