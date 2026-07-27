using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetApplication;

internal sealed record GetApplicationResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    ApplicationStatus Status,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Ticket #66: stage-change history surfaced directly on the applicant record, ordered oldest
    // first. Distinct from the cross-cutting IAuditEvent log (see RecruitmentAudit's
    // ApplicationStageChangedAuditEvent) — this is domain-specific data, not a general audit trail.
    IReadOnlyList<ApplicationStageHistoryItem> StageHistory);

internal sealed record ApplicationStageHistoryItem(
    Guid Id,
    ApplicationStatus PreviousStage,
    ApplicationStatus NewStage,
    Guid? ChangedByUserId,
    string? Notes,
    DateTimeOffset ChangedAt);
