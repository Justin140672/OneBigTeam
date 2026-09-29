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
    int TotalTasks,
    int ResolvedTasks,
    int ProgressPercent,
    int RequiredObligationsTotal,
    int RequiredObligationsResolved,
    int TotalObligationsCount,
    int TotalObligationsResolved,
    bool IsOverdueAfterDeparture,
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
    DateTimeOffset? SkippedAt,
    Guid? OpenTaskId,
    Guid? AssetAssignmentId);
