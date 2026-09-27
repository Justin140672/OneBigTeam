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
    // Ticket 1: CV review notes recorded against this application via the Review CV workflow.
    // Internal recruitment Ticket 1: CvDocumentId/CvFileName/CvContentType/CvFileSize/CvUploadedAt
    // describe the exact CV SUBMITTED with this application (Application.CvDocumentId). They are all
    // null when no CV was captured (e.g. historic applications) — in that case the candidate's
    // current CV, if any, is available via the CurrentCandidateCv* fields below and must be labelled
    // as current/legacy material, never as the submitted CV.
    string? CvReviewNotes,
    DateTimeOffset? CvReviewedAt,
    Guid? CvReviewedByUserId,
    Guid? CvDocumentId,
    string? CvFileName,
    string? CvContentType,
    long? CvFileSize,
    DateTimeOffset? CvUploadedAt,
    // Ticket #66: stage-change history surfaced directly on the candidate's application record, ordered oldest
    // first. Distinct from the cross-cutting IAuditEvent log (see RecruitmentAudit's
    // ApplicationStageChangedAuditEvent) — this is domain-specific data, not a general audit trail.
    IReadOnlyList<ApplicationStageHistoryItem> StageHistory,
    // Ticket 2: offer terms and response lifecycle recorded on this application. All null until an
    // offer is made via OfferCandidate. OfferResponseStatus is one of AwaitingResponse / Accepted /
    // Declined / Withdrawn.
    decimal? OfferedSalary = null,
    string? OfferedSalaryFrequency = null,
    DateOnly? OfferedStartDate = null,
    DateOnly? OfferDate = null,
    string? OfferNotes = null,
    string? OfferResponseStatus = null,
    DateTimeOffset? OfferMadeAt = null,
    DateTimeOffset? OfferRespondedAt = null,
    // Ticket 2 (optimistic concurrency): round-trip as ExpectedVersion on SetApplicationCv.
    int Version = 1,
    // Internal recruitment Ticket 1: the candidate's current (most recently uploaded) CV, independent
    // of what was submitted with this application. May equal CvDocumentId. Null when the candidate has
    // no uploaded CV.
    Guid? CurrentCandidateCvDocumentId = null,
    string? CurrentCandidateCvFileName = null,
    string? CurrentCandidateCvContentType = null,
    long? CurrentCandidateCvFileSize = null,
    DateTimeOffset? CurrentCandidateCvUploadedAt = null,
    // [P1] Malware scan state (Pending | Scanning | Clean | Infected | Failed) of the submitted CV and of
    // the current CV. Null when the corresponding CV is absent. Only "Clean" is downloadable/viewable.
    string? CvScanStatus = null,
    string? CurrentCandidateCvScanStatus = null,
    // Internal recruitment Ticket 6: true only when Source == Internal (the authoritative indicator).
    // EmployeeId is the applicant's linked employee and is populated ONLY for internal applications —
    // it stays null for external candidates, including those later hired (whose Candidate.EmployeeId
    // is set by HireCandidate), so an external application is never retroactively labelled internal.
    bool IsInternal = false,
    Guid? EmployeeId = null,
    // Internal recruitment Ticket 7: internal appointment progress ("Pending" / "Completed"; null when
    // none started) and, once completed, the effective date of the employee change.
    string? InternalAppointmentStatus = null,
    DateOnly? InternalAppointmentEffectiveDate = null);

internal sealed record ApplicationStageHistoryItem(
    Guid Id,
    Guid? PreviousStageId,
    Guid NewStageId,
    Guid? ChangedByUserId,
    string? Notes,
    DateTimeOffset ChangedAt);
