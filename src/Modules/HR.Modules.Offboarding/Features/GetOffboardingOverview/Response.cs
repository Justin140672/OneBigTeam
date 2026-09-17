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
    // SPEC-OFF-01: "X of Y required obligations resolved" / "X of Y total obligations resolved",
    // excluding Cancelled obligations from both counts.
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
    // Leaving/Offboarding unified workspace: lets the UI deep-link straight to the Tasks-module
    // task for this obligation (IOpenTaskBySourceEntityReader, keyed on the OffboardingTask's own
    // id — see OffboardingTaskSynchronizer, which always passes sourceEntityId: task.Id). Null
    // when no open task exists (not yet synced, or already terminal).
    Guid? OpenTaskId,
    // OFF-04: lets the UI offer "open source record" for asset-return obligations without the
    // client having to know which obligations are asset-backed by title-matching.
    Guid? AssetAssignmentId);
