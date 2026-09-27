namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListApplicationsForVacancyResponse(List<ApplicationListItemModel> Items);

public record ApplicationListItemModel(
    Guid Id,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    Guid CurrentStageId,
    string? InterviewOutcome,
    bool IsWithdrawn,
    DateTimeOffset AppliedAt,
    // Ticket #2: offer response tracking. Null until an offer has been made.
    // "AwaitingResponse" / "Accepted" / "Declined" / "Withdrawn".
    string? OfferResponseStatus = null,
    decimal? OfferedSalary = null,
    DateOnly? OfferedStartDate = null,
    // Internal recruitment Ticket 6: true only when Source == Internal (authoritative — never infer
    // from a candidate's EmployeeId). EmployeeId is populated only for internal applications.
    bool IsInternal = false,
    Guid? EmployeeId = null,
    // Internal recruitment Ticket 7: "Pending" | "Completed" | null (no appointment attempted yet).
    string? InternalAppointmentStatus = null,
    DateOnly? InternalAppointmentEffectiveDate = null);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetApplicationResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    Guid CurrentStageId,
    string CurrentStageName,
    string? InterviewOutcome,
    string? Notes,
    // Ticket #99: candidate-initiated withdrawal, orthogonal to CurrentStageId.
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Ticket #78 — both null for applications recorded before this concept existed.
    string? Source,
    Guid? SourceExternalRecruiterId,
    string? SourceExternalRecruiterAgencyName,
    // Ticket #66: stage-change history, ordered oldest first.
    IReadOnlyList<ApplicationStageHistoryItemModel>? StageHistory = null,
    // Ticket #1: Candidate CV upload & review. All null for applications with no CV / no review yet.
    string? CvReviewNotes = null,
    DateTimeOffset? CvReviewedAt = null,
    Guid? CvReviewedByUserId = null,
    // Internal recruitment Ticket 1: Cv* describe ONLY the CV submitted with this application —
    // null when none was captured (e.g. historic applications). Never substitute the candidate's
    // current CV here; use the CurrentCandidateCv* fields and label them as such.
    Guid? CvDocumentId = null,
    string? CvFileName = null,
    string? CvContentType = null,
    long? CvFileSize = null,
    DateTimeOffset? CvUploadedAt = null,
    // Ticket #2: offer terms & response.
    string? OfferResponseStatus = null,
    decimal? OfferedSalary = null,
    DateOnly? OfferedStartDate = null,
    // Optimistic concurrency: round-trip as ExpectedVersion on SetApplicationCv.
    int Version = 1,
    // Internal recruitment Ticket 1: the candidate's most recently uploaded CV, independent of the
    // submitted CV (may equal CvDocumentId). Null when the candidate has no uploaded CV.
    Guid? CurrentCandidateCvDocumentId = null,
    string? CurrentCandidateCvFileName = null,
    string? CurrentCandidateCvContentType = null,
    long? CurrentCandidateCvFileSize = null,
    DateTimeOffset? CurrentCandidateCvUploadedAt = null,
    // Malware-scan state ("Pending"/"Scanning"/"Clean"/"Infected"/"Failed") of the submitted CV and of
    // the candidate's current CV respectively. Only "Clean" may be viewed/downloaded.
    string? CvScanStatus = null,
    string? CurrentCandidateCvScanStatus = null,
    // Internal recruitment Ticket 6: true only when Source == Internal. EmployeeId is populated only
    // for internal applications.
    bool IsInternal = false,
    Guid? EmployeeId = null,
    // Internal recruitment Ticket 7: "Pending" | "Completed" | null (no appointment attempted yet).
    string? InternalAppointmentStatus = null,
    DateOnly? InternalAppointmentEffectiveDate = null);

// ── INTERNAL RECRUITMENT TICKET 1: SUBMITTED CV ──────────────────────────────

// PUT .../applications/{applicationId}/cv — null CvDocumentId removes the submitted-CV reference.
public record SetApplicationCvRequest(Guid? CvDocumentId, int ExpectedVersion);

public record SetApplicationCvResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid? CvDocumentId,
    int Version,
    DateTimeOffset UpdatedAt);

// ── TICKET #1: CV REVIEW ──────────────────────────────────────────────────────

public record SaveCvReviewNotesRequest(Guid CompanyId, Guid VacancyId, Guid ApplicationId, string? CvReviewNotes);

public record SaveCvReviewNotesResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    string? CvReviewNotes,
    DateTimeOffset? CvReviewedAt,
    Guid? CvReviewedByUserId,
    DateTimeOffset UpdatedAt);

public record MoveApplicationForwardRequest(Guid CompanyId, Guid VacancyId, Guid ApplicationId, string? CvReviewNotes);

public record MoveApplicationForwardResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid PreviousStageId,
    Guid CurrentStageId,
    string CurrentStageName,
    string? CvReviewNotes,
    DateTimeOffset UpdatedAt);

public record ApplicationStageHistoryItemModel(
    Guid Id,
    Guid? PreviousStageId,
    Guid NewStageId,
    Guid? ChangedByUserId,
    string? Notes,
    DateTimeOffset ChangedAt);

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreateApplicationRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid CandidateId,
    string? Notes,
    // Ticket #78. Both optional; SourceExternalRecruiterId is required if and only if
    // Source == "ExternalRecruiter" (enforced server-side by CreateApplicationValidator).
    string? Source = null,
    Guid? SourceExternalRecruiterId = null,
    // Internal recruitment Ticket 1: optional candidate CV document (Kind = Cv) submitted with this application.
    Guid? CvDocumentId = null);

public record CreateApplicationResponse(
    Guid Id,
    Guid CompanyId,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    string? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Source = null,
    Guid? SourceExternalRecruiterId = null,
    Guid? CvDocumentId = null);

// ── INTERNAL RECRUITMENT TICKET 3: NEW CANDIDATE + APPLICATION ───────────────

// Fields of POST .../vacancies/{vacancyId}/applications/new-candidate (multipart/form-data — the
// service maps this to form parts, omitting null optionals). The optional CV file is passed
// separately as an IBrowserFile.
public record CreateCandidateApplicationRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? ResumeUrl,
    string? Notes,
    // Enum name: Unspecified|Direct|Referral|ExternalRecruiter|JobBoard|CareersSite (null = omitted).
    string? Source,
    // Required iff Source == "ExternalRecruiter".
    Guid? SourceExternalRecruiterId);

// 201 body.
public record CreateCandidateApplicationResponse(
    Guid CandidateId,
    Guid ApplicationId,
    Guid CompanyId,
    Guid VacancyId,
    string FirstName,
    string LastName,
    string Email,
    Guid CurrentStageId,
    Guid? CvDocumentId,
    string? Source,
    Guid? SourceExternalRecruiterId,
    DateTimeOffset AppliedAt);

// The existing candidate identified by a 409 { code: "candidate_email_exists" } body.
public record DuplicateCandidateModel(
    Guid CandidateId,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive)
{
    public string FullName => $"{FirstName} {LastName}";
}

// Exactly one of Created / Duplicate / Error is set.
public sealed record CreateCandidateApplicationResult(
    CreateCandidateApplicationResponse? Created,
    DuplicateCandidateModel? Duplicate,
    string? Error)
{
    public static CreateCandidateApplicationResult Success(CreateCandidateApplicationResponse created) => new(created, null, null);
    public static CreateCandidateApplicationResult DuplicateEmail(DuplicateCandidateModel duplicate) => new(null, duplicate, null);
    public static CreateCandidateApplicationResult Failure(string error) => new(null, null, error);
}

// ── STATUS TRANSITIONS ────────────────────────────────────────────────────────

// Dedicated response for the Withdraw action — WithdrawApplicationResponse (HR.Modules.Recruitment)
// carries WithdrawnAt but no RejectionReason.
public record WithdrawApplicationResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    string? InterviewOutcome,
    string? Notes,
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record RejectCandidateRequest(Guid CompanyId, Guid VacancyId, Guid ApplicationId, string? RejectionReason);

// Dedicated response for the Reject action — RejectCandidateResponse (HR.Modules.Recruitment)
// carries RejectionReason but no WithdrawnAt.
public record RejectCandidateResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    string? InterviewOutcome,
    string? Notes,
    string? RejectionReason,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// Dedicated response for the Offer action (rather than the generic ApplicationActionResponse) so the
// linked Position Profile's read-only employment defaults are available to the UI while HR decides to
// make an offer. See OfferCandidateResponse (HR.Modules.Recruitment) for the authoritative shape.
public record OfferCandidateRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid ApplicationId,
    // Ticket #2: all optional offer terms.
    decimal? OfferedSalary = null,
    string? OfferedSalaryFrequency = null, // "Annual" | "Hourly" | "Daily"
    DateOnly? ProposedStartDate = null,
    DateOnly? OfferDate = null, // defaults to today server-side when omitted
    string? OfferNotes = null);

public record OfferCandidateResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    string? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid PositionProfileId,
    string? PositionProfileTitle,
    decimal? SalaryMin,
    decimal? SalaryMax,
    string? SalaryType,
    string? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    int? ProbationMonthsOverride,
    Guid? DefaultLeavePolicyId,
    string? LocationName,
    // Ticket #2: offer terms & response echoed back.
    decimal? OfferedSalary = null,
    string? OfferedSalaryFrequency = null,
    DateOnly? ProposedStartDate = null,
    DateOnly? OfferDate = null,
    string? OfferNotes = null,
    string? OfferResponseStatus = null,
    DateTimeOffset? OfferMadeAt = null,
    DateTimeOffset? OfferRespondedAt = null);

// ── TICKET #2: OFFER RESPONSE ─────────────────────────────────────────────────

public record RespondToOfferRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid ApplicationId,
    string Status); // "Accepted" | "Declined" | "Withdrawn"

public record RespondToOfferResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    string? OfferResponseStatus,
    decimal? OfferedSalary,
    string? OfferedSalaryFrequency,
    DateOnly? ProposedStartDate,
    DateOnly? OfferDate,
    string? OfferNotes,
    DateTimeOffset UpdatedAt);

// Department, Location and Position Profile are no longer independently-entered fields — the hired
// employee is always assigned to the Vacancy's own linked Position Profile (and the Department/Location
// derived from it), resolved server-side by HireCandidateHandler. See that handler's remarks.
// NOTE: AddressLine1/2, City, County and PostCode below are NOT yet accepted by
// HR.Modules.Recruitment's HireCandidate Request/Handler or by
// HR.Modules.Employees.Contracts.EmployeeProvisioningRequest (checked 2026-08-24) — the API will
// silently ignore these fields until the backend is extended to accept and thread them through to
// employee provisioning. Included here so the UI is ready end-to-end once that backend work lands.
public record HireCandidateRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid ApplicationId,
    // Ticket #2: optional — server falls back to the accepted offer's ProposedStartDate.
    DateOnly? StartDate,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    string? GenderOther,
    string EmployeeNumber,
    Guid EmploymentTypeId,
    Guid? ManagerId,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? County = null,
    string? PostCode = null);

public record HireCandidateResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid EmployeeId,
    Guid CurrentStageId,
    string? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── INTERNAL RECRUITMENT TICKET 7: APPOINT INTERNAL CANDIDATE ─────────────────
// Mirrors HR.Modules.Recruitment.Features.AppointInternalCandidate
// (POST .../applications/{applicationId}/appoint). Position profile, department and location are
// never sent — the server derives them from the vacancy. Exactly one of ManagerId / NoManager is set.

public record AppointInternalCandidateRequest(
    DateOnly? EffectiveDate,
    bool ConfirmBackdatedEffectiveDate,
    Guid? ManagerId,
    bool NoManager,
    bool CreateCompensationChange,
    string? CompensationSalaryType = null,
    decimal? CompensationSalary = null,
    string? CompensationCurrency = null,
    decimal? CompensationHoursPerWeek = null,
    decimal? CompensationFte = null,
    string? CompensationNotes = null);

public record AppointInternalCandidateResponse(
    Guid ApplicationId,
    Guid VacancyId,
    Guid CandidateId,
    Guid EmployeeId,
    Guid PromotionId,
    Guid CurrentStageId,
    Guid PositionProfileId,
    Guid DepartmentId,
    Guid LocationId,
    Guid? ManagerId,
    DateOnly EffectiveDate,
    // False when the effective date is in the future: the change is scheduled and applied on that date.
    bool IsApplied,
    Guid? CompensationId,
    string AppointmentStatus);

// ── DASHBOARD: APPLICATIONS BY STATUS ───────────────────────────────────────────

public record GetApplicationsByStatusResponse(IReadOnlyList<ApplicationByStatusItem> Items);

public record ApplicationByStatusItem(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid VacancyId,
    string VacancyTitle,
    DateTimeOffset AppliedAt);

// ── INTERNAL RECRUITMENT TICKET 6: APPLICATION SEARCH ─────────────────────────
// Mirrors HR.Modules.Recruitment.Features.SearchApplications
// (GET api/companies/{companyId}/recruitment/applications/search).

public record SearchApplicationsResponse(
    List<ApplicationSearchItemModel> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

public record ApplicationSearchItemModel(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    Guid VacancyId,
    string VacancyTitle,
    Guid CurrentStageId,
    DateTimeOffset AppliedAt,
    string? CurrentStageName = null,
    bool IsWithdrawn = false,
    // True only when Source == Internal. EmployeeId is populated only for internal applications.
    bool IsInternal = false,
    Guid? EmployeeId = null);

// ── INTERNAL RECRUITMENT TICKET 6: APPLICATION TYPE FILTER ────────────────────
// UI-side choice for the "Application type" / "Applications" dropdowns (vacancy applications tab and
// the recruitment reports). An explicit "All" item is used instead of a clear button, and maps to
// omitting the API's isInternal query param.

public enum ApplicationTypeFilter
{
    All = 0,
    Internal = 1,
    External = 2,
}

public sealed record ApplicationTypeFilterOption(ApplicationTypeFilter Value, string Label);

public static class ApplicationTypeFilterExtensions
{
    // null = all (omit the query param); true = internal only; false = external only.
    public static bool? ToIsInternal(this ApplicationTypeFilter filter) => filter switch
    {
        ApplicationTypeFilter.Internal => true,
        ApplicationTypeFilter.External => false,
        _ => null,
    };
}
