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
    int TotalTasks,
    int ResolvedTasks,
    int ProgressPercent,
    int RequiredObligationsTotal,
    int RequiredObligationsResolved,
    int TotalObligationsCount,
    int TotalObligationsResolved,
    bool IsOverdueAfterDeparture,
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
    DateTimeOffset? SkippedAt,
    Guid? OpenTaskId,
    Guid? AssetAssignmentId);

public sealed record OffboardingOverviewLookupResult(OffboardingOverviewModel? Overview, bool Failed)
{
    public static OffboardingOverviewLookupResult SuccessResult(OffboardingOverviewModel? overview) => new(overview, false);
    public static OffboardingOverviewLookupResult FailedResult() => new(null, true);
}

public sealed record WaiveOffboardingTaskRequest(Guid CompanyId, string Reason);

public sealed record WaiveOffboardingTaskResponse(
    Guid Id,
    string Status,
    DateTimeOffset? SkippedAt,
    Guid? SkippedByUserId);

public sealed record StartOffboardingResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    string Status,
    string? Notes,
    IReadOnlyList<Guid> GeneratedTaskIds,
    DateTimeOffset CreatedAt);
