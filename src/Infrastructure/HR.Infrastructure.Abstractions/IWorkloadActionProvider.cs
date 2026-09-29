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
    string? OwnerLabel = null)
{
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
