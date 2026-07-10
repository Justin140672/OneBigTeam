namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListApplicationsForVacancyResponse(List<ApplicationListItemModel> Items);

public record ApplicationListItemModel(
    Guid Id,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    string Status,
    string? InterviewOutcome,
    DateTimeOffset AppliedAt);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetApplicationResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    string Status,
    string? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreateApplicationRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid CandidateId,
    string? Notes);

public record CreateApplicationResponse(
    Guid Id,
    Guid CompanyId,
    Guid VacancyId,
    Guid CandidateId,
    string Status,
    string? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── STATUS TRANSITIONS ────────────────────────────────────────────────────────

public record ApplicationActionResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    string Status,
    string? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record RejectCandidateRequest(Guid CompanyId, Guid VacancyId, Guid ApplicationId, string? RejectionReason);

public record HireCandidateRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid ApplicationId,
    DateOnly StartDate,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    string? GenderOther,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    Guid? ManagerId);

public record HireCandidateResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid EmployeeId,
    string Status,
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
