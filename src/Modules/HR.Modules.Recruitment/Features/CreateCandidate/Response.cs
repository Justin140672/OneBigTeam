namespace HR.Modules.Recruitment.Features.CreateCandidate;

internal sealed record CreateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
