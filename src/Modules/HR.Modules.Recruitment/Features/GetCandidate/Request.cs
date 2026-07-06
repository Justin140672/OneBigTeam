namespace HR.Modules.Recruitment.Features.GetCandidate;

internal sealed record GetCandidateRequest
{
    public Guid CompanyId { get; init; }
    public Guid CandidateId { get; init; }
}
