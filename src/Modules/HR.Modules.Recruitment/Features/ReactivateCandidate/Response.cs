namespace HR.Modules.Recruitment.Features.ReactivateCandidate;

internal sealed record ReactivateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    bool IsActive,
    DateTimeOffset? ReactivatedAt,
    Guid? ReactivatedByUserId,
    DateTimeOffset UpdatedAt);
