namespace HR.Modules.Recruitment.Features.UpdateCandidate;

internal sealed record UpdateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version);
