namespace HR.Modules.Recruitment.Features.DeactivateCandidate;

internal sealed record DeactivateCandidateResponse(
    Guid Id,
    Guid CompanyId,
    bool IsActive,
    DateTimeOffset? DeactivatedAt,
    Guid? DeactivatedByUserId,
    string? DeactivationReason,
    DateTimeOffset UpdatedAt);
