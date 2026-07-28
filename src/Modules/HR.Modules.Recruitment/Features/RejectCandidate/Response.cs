using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.RejectCandidate;

internal sealed record RejectCandidateResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    string? RejectionReason,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
