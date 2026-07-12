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
