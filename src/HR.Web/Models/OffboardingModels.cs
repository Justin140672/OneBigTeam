namespace HR.Web.Models;

// Deliberately minimal — used only to decide whether the Employee Overview page should show its
// Offboarding tab at all; see OffboardingService.GetStatusAsync.
public sealed record OffboardingStatusModel(bool HasPlan, string? Status);

public sealed record OffboardingOverviewModel(
    Guid EmployeeId,
    bool HasPlan,
    string? PlanStatus,
    DateOnly? LastWorkingDay,
    string? Notes,
    bool IsBackdated,
    bool RequiresHrReconciliation,
    bool HasIncompleteOffboardingAtDeparture,
    // OFF-07: server-computed (OffboardingProgressCalculator) — display these directly rather than
    // recomputing locally, so progress can never drift from the Reporting module's own numbers.
    int TotalTasks,
    int ResolvedTasks,
    int ProgressPercent,
    IReadOnlyList<OffboardingTaskOverviewItem> Tasks);

public sealed record OffboardingTaskOverviewItem(
    Guid Id,
    string Title,
    string? Description,
    string AssignTo,
    string Status,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool RequiresHrConfirmation,
    bool IsMandatory,
    string? SkipReason,
    Guid? SkippedByUserId,
    DateTimeOffset? SkippedAt);

public sealed record StartOffboardingResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    string Status,
    string? Notes,
    IReadOnlyList<Guid> GeneratedTaskIds,
    DateTimeOffset CreatedAt);
