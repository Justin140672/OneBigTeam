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
    DateTimeOffset AppliedAt);

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
    IReadOnlyList<ApplicationStageHistoryItemModel>? StageHistory = null);

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
    Guid? SourceExternalRecruiterId = null);

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
    Guid? SourceExternalRecruiterId = null);

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
    string? LocationName);

// Department, Location and Position Profile are no longer independently-entered fields — the hired
// employee is always assigned to the Vacancy's own linked Position Profile (and the Department/Location
// derived from it), resolved server-side by HireCandidateHandler. See that handler's remarks.
public record HireCandidateRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid ApplicationId,
    DateOnly StartDate,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    string? GenderOther,
    string EmployeeNumber,
    Guid EmploymentTypeId,
    Guid? ManagerId);

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
