using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetApplication;

internal sealed record GetApplicationResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    Guid CurrentStageId,
    string CurrentStageName,
    InterviewOutcome? InterviewOutcome,
    string? Notes,
    // Ticket #99: candidate-initiated withdrawal, orthogonal to CurrentStageId — see
    // Application.WithdrawnAt's remarks.
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ApplicationSource? Source,
    Guid? SourceExternalRecruiterId,
    // Denormalised for display convenience so the UI doesn't need a second round trip; null when
    // Source != ExternalRecruiter or the recruiter row could not be resolved (should not normally
    // happen since ExternalRecruiter rows are never deleted, only deactivated).
    string? SourceExternalRecruiterAgencyName,
    // Ticket 1: CV review notes recorded against this application via the Review CV workflow, plus a
    // summary of the candidate's current (most recently uploaded) CV document. CvDocumentId is null
    // when the candidate has no uploaded CV — the legacy Candidate.ResumeUrl link (see
    // GetCandidate) then remains the only CV reference for historical records.
    string? CvReviewNotes,
    DateTimeOffset? CvReviewedAt,
    Guid? CvReviewedByUserId,
    Guid? CvDocumentId,
    string? CvFileName,
    string? CvContentType,
    long? CvFileSize,
    DateTimeOffset? CvUploadedAt,
    // Ticket #66: stage-change history surfaced directly on the applicant record, ordered oldest
    // first. Distinct from the cross-cutting IAuditEvent log (see RecruitmentAudit's
    // ApplicationStageChangedAuditEvent) — this is domain-specific data, not a general audit trail.
    IReadOnlyList<ApplicationStageHistoryItem> StageHistory);

internal sealed record ApplicationStageHistoryItem(
    Guid Id,
    Guid? PreviousStageId,
    Guid NewStageId,
    Guid? ChangedByUserId,
    string? Notes,
    DateTimeOffset ChangedAt);
