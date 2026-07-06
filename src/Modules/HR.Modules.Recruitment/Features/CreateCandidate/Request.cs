namespace HR.Modules.Recruitment.Features.CreateCandidate;

internal sealed record CreateCandidateRequest
{
    public Guid CompanyId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? ResumeUrl { get; init; }
}
