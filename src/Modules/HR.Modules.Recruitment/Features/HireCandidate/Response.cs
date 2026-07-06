using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.HireCandidate;

internal sealed record HireCandidateResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    ApplicationStatus Status,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
