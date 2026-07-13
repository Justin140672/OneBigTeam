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
    DateTimeOffset UpdatedAt);

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
    DateOnly DueDate);

public sealed record UpcomingProbationReviewsResponse(IReadOnlyList<UpcomingProbationReviewItem> Items);

// Self-scoped equivalent of ProbationRecordModel, backed by the "authenticated"-only
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
