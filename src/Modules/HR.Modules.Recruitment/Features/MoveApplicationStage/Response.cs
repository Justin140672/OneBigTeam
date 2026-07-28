using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.MoveApplicationStage;

internal sealed record MoveApplicationStageResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
