namespace HR.Web.Models;

// Deliberately minimal — used only to decide whether the Employee Overview page should show its
// Probation tab at all; see ProbationService.GetStatusAsync.
public sealed record ProbationStatusModel(bool HasRecord, string? Status);

public sealed record ProbationRecordModel(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid ManagerEmployeeId,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    string Status,
    string? Notes,
    string? ExtensionReason,
    DateOnly? DecisionDate,
    string? OutcomeNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Ticket 17: optimistic-concurrency token — see UpdateProbationRecordRequest.ExpectedVersion
    // and HR.Web.Components.Pages.EditSectionBase. Defaulted so existing GetProbationRecordByEmployee
    // JSON payloads (which already include Version) continue to deserialize unchanged.
    int Version = 0);

// Ticket 17: request/response for the HR Administrator "administrative correction" edit
// (PUT /api/companies/{companyId}/probation-records/{id}) — manager, expected end date and notes
// ONLY, per ProbationRecord.ApplyAdministrativeCorrection. Status, extension reason, decision date/
// maker and outcome notes are workflow-owned (CompleteProbationReview) and deliberately excluded.
public sealed record UpdateProbationRecordApiRequest(
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid ManagerEmployeeId,
    DateOnly ExpectedEndDate,
    string? Notes,
    int? ExpectedVersion);

public sealed record UpdateProbationRecordApiResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid ManagerEmployeeId,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    string Status,
    string? Notes,
    string? ExtensionReason,
    Guid? DecisionMakerEmployeeId,
    DateOnly? DecisionDate,
    string? OutcomeNotes,
    DateTimeOffset UpdatedAt,
    int Version);

public sealed record ProbationReviewModel(
    Guid Id,
    Guid ProbationRecordId,
    string ReviewType,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? CompletedAt,
    string? Outcome,
    string? Notes);

public sealed record ProbationReviewsResponse(IReadOnlyList<ProbationReviewModel> Items);

public sealed record UpcomingProbationReviewItem(
    Guid ReviewId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    string ReviewType,
    DateOnly DueDate,
    Guid? TaskId = null);

public sealed record UpcomingProbationReviewsResponse(IReadOnlyList<UpcomingProbationReviewItem> Items);

// Self-scoped equivalent of ProbationRecordModel, backed by the "role:employee"-only
// employees/me/probation-status endpoint — see ProbationService.GetMyProbationStatusAsync.
// Used to fix the Probation badge silently 403ing for a real employee viewing their own profile.
public sealed record MyProbationStatusModel(
    bool HasRecord,
    Guid? Id,
    DateOnly? StartDate,
    DateOnly? ExpectedEndDate,
    string? Status,
    DateOnly? DecisionDate,
    string? OutcomeNotes);

public sealed record ProbationReviewDetailModel(
    Guid Id,
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    string ReviewType,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? CompletedAt,
    string? Notes,
    DateOnly RecordStartDate,
    DateOnly RecordExpectedEndDate,
    string RecordStatus);
