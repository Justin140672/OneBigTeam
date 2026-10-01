using System.Security.Claims;

namespace HR.Infrastructure.Abstractions;

public enum WorkloadScope
{
    Manager,

    Hr
}

public interface IWorkloadActionProvider
{
    string ActionCategory { get; }

    Task<IReadOnlyList<WorkloadAction>> GetActionsAsync(
        Guid companyId,
        ClaimsPrincipal caller,
        WorkloadScope requestedScope,
        CancellationToken cancellationToken);
}

public enum WorkloadActionUrgency
{
    Overdue,
    DueToday,
    DueThisWeek,
    Upcoming
}

public enum WorkloadActionability
{
    CanAct,
    VisibilityOnly,
    Unavailable
}

public sealed record WorkloadAction(
    Guid EmployeeId,
    string EmployeeName,
    string? Department,
    string ActionType,
    string ActionCategory,
    DateOnly? DueDate,
    string? AssignedTo,
    string Status,
    string DeepLinkUrl,
    WorkloadActionUrgency Urgency = WorkloadActionUrgency.Upcoming,
    Guid? TaskId = null,
    bool IsOwnerActionable = true,
    string? OwnerLabel = null,
    WorkloadActionability? ActionabilityOverride = null,
    string? VisibilityReason = null,
    string? MonitoringUrl = null)
{
    public WorkloadActionability Actionability =>
        ActionabilityOverride ?? (IsOwnerActionable ? WorkloadActionability.CanAct : WorkloadActionability.VisibilityOnly);

    public static WorkloadActionUrgency ComputeUrgency(DateOnly? dueDate, DateOnly today)
    {
        if (dueDate is null)
            return WorkloadActionUrgency.Upcoming;

        if (dueDate < today)
            return WorkloadActionUrgency.Overdue;

        if (dueDate == today)
            return WorkloadActionUrgency.DueToday;

        return dueDate <= today.AddDays(7)
            ? WorkloadActionUrgency.DueThisWeek
            : WorkloadActionUrgency.Upcoming;
    }
}

public readonly record struct WorkloadOwnerDecision(
    WorkloadActionability Actionability,
    string? OwnerLabel,
    string? VisibilityReason)
{
    public bool IsOwnerActionable => Actionability == WorkloadActionability.CanAct;
}

public static class WorkloadOwnership
{
    public const string HrQueueLabel = "HR queue";

    public static WorkloadOwnerDecision ForHrViewer(
        Guid? assigneeId,
        Guid? viewerEmployeeId,
        bool unassignedBelongsToHr,
        string? assigneeName,
        string fallbackOwnerLabel)
    {
        if (assigneeId is null)
        {
            return unassignedBelongsToHr
                ? new WorkloadOwnerDecision(WorkloadActionability.CanAct, null, null)
                : new WorkloadOwnerDecision(
                    WorkloadActionability.VisibilityOnly,
                    fallbackOwnerLabel,
                    $"Shown so you can monitor it. {fallbackOwnerLabel}.");
        }

        if (viewerEmployeeId is { } viewer && viewer == assigneeId)
            return new WorkloadOwnerDecision(WorkloadActionability.CanAct, null, null);

        var label = string.IsNullOrWhiteSpace(assigneeName) ? fallbackOwnerLabel : $"Assigned to {assigneeName}";
        return new WorkloadOwnerDecision(
            WorkloadActionability.VisibilityOnly,
            label,
            $"Shown so you can monitor it. {label}.");
    }
}
