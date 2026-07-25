namespace HR.Web.Models;

// Deliberately minimal — used only to decide whether the Employee Overview page should show its
// Onboarding tab at all; see OnboardingService.GetStatusAsync.
public sealed record OnboardingStatusModel(bool HasPlan, string? Status);

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

// Self-scoped, backed by the "role:employee"-only employees/me/onboarding-status endpoint — see
// OnboardingService.GetMyOnboardingStatusAsync. Backs MyProfileOverviewTab's Onboarding Progress card.
public sealed record MyOnboardingStatusModel(
    bool HasPlan,
    string? PlanStatus,
    DateOnly? StartDate,
    int TotalTasks,
    int CompletedTasks,
    IReadOnlyList<MyOnboardingTaskItem> Tasks);

public sealed record MyOnboardingTaskItem(
    Guid Id,
    string Title,
    string Status,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt);

public sealed record TeamOnboardingListModel(IReadOnlyList<TeamOnboardingItem> Items);

public sealed record TeamOnboardingItem(
    Guid EmployeeId,
    string EmployeeName,
    string PlanStatus,
    DateOnly StartDate,
    int TotalTasks,
    int CompletedTasks,
    int PercentComplete);
