namespace HR.Modules.Recruitment.Features.UpdateCandidate;

internal sealed record UpdateCandidateRequest
{
    public Guid CompanyId { get; init; }
    public Guid CandidateId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? ResumeUrl { get; init; }
}
