using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.MoveApplicationStage;

internal sealed record MoveApplicationStageResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    ApplicationStatus Status,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
