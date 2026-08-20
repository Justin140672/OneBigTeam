namespace HR.Modules.Recruitment.Features.ListCandidates;

internal sealed record ListCandidatesResponse(
    IReadOnlyList<CandidateListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record CandidateListItem(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    bool IsActive,
    DateTimeOffset CreatedAt);
