namespace HR.Modules.Offboarding.Features.GetOffboardingOverview;

internal sealed record GetOffboardingOverviewResponse(
    Guid EmployeeId,
    bool HasPlan,
    string? PlanStatus,
    DateOnly? LastWorkingDay,
    string? Notes,
    bool IsBackdated,
    bool RequiresHrReconciliation,
    bool HasIncompleteOffboardingAtDeparture,
    // OFF-07: server-computed via OffboardingProgressCalculator — the single source of truth for
    // plan progress. Consumers (the Blazor Offboarding tab) should display these rather than
    // recomputing their own counts, so progress can never drift between UI/reports/cross-module
    // readers again.
    int TotalTasks,
    int ResolvedTasks,
    int ProgressPercent,
    IReadOnlyList<OffboardingTaskOverviewItem> Tasks);

internal sealed record OffboardingTaskOverviewItem(
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
