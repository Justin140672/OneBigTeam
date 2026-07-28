using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.WithdrawApplication;

internal sealed record WithdrawApplicationResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
