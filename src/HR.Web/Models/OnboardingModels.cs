namespace HR.Web.Models;

public sealed record OnboardingOverviewModel(
    Guid EmployeeId,
    bool HasPlan,
    string? PlanStatus,
    DateOnly? StartDate,
    IReadOnlyList<OnboardingTaskOverviewItem> Tasks,
    IReadOnlyList<OutstandingDocumentRequestItem> OutstandingDocumentRequests,
    IReadOnlyList<OutstandingAssetAcknowledgementItem> OutstandingAssetAcknowledgements,
    ProbationSummaryItem? Probation);

public sealed record OnboardingTaskOverviewItem(
    Guid Id,
    string Title,
    string Status,
    DateOnly? DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);

public sealed record OutstandingDocumentRequestItem(
    Guid Id,
    string DocumentTypeName,
    DateOnly? DueDate,
    bool IsMandatory);

public sealed record OutstandingAssetAcknowledgementItem(
    Guid AssetAssignmentId,
    Guid AssetId,
    string AssetLabel,
    DateTimeOffset AssignedAt);

public sealed record ProbationSummaryItem(
    string Status,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    DateOnly? DecisionDate);

public sealed record TeamOnboardingListModel(IReadOnlyList<TeamOnboardingItem> Items);

public sealed record TeamOnboardingItem(
    Guid EmployeeId,
    string EmployeeName,
    string PlanStatus,
    DateOnly StartDate,
    int TotalTasks,
    int CompletedTasks,
    int PercentComplete);
