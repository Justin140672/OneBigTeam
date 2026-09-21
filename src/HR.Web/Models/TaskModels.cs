namespace HR.Web.Models;

public sealed record GetOutstandingTaskCountResponse(int Count);

/// <summary>
/// Outcome of loading a single task for <c>TaskViewDialog</c>. Distinguishes a task that genuinely
/// does not exist (or that the caller can no longer access — 404/403) from a recoverable/transient
/// load failure (network error, timeout, 5xx), so the dialog can offer a retry for the latter
/// rather than presenting every failure as "task not found".
/// </summary>
public enum TaskLoadStatus
{
    Loaded,
    NotFound,
    Failed
}

public sealed record TaskFetchResult(TaskLoadStatus Status, TaskDetailModel? Task)
{
    public static TaskFetchResult Loaded(TaskDetailModel task) => new(TaskLoadStatus.Loaded, task);
    public static readonly TaskFetchResult NotFound = new(TaskLoadStatus.NotFound, null);
    public static readonly TaskFetchResult Failed = new(TaskLoadStatus.Failed, null);
}

public sealed record TaskListResponse(
    IReadOnlyList<TaskListItem> Items,
    int TotalCount = 0,
    int PageNumber = 1,
    int PageSize = 20,
    int TotalPages = 0);

public sealed record UnassignedTaskListResponse(IReadOnlyList<UnassignedTaskItem> Items);

public sealed record UnassignedTaskItem(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    string Source,
    string ActionType,
    DateOnly? DueDate,
    Guid? SourceEntityId,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);

public sealed record TaskDetailModel(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    string Source,
    string ActionType,
    DateOnly? DueDate,
    Guid? AssignedEmployeeId,
    Guid? AssignedUserId,
    Guid? SourceEntityId,
    Guid CreatedBy,
    Guid? CompletedBy,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TaskListItem(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    string Source,
    string ActionType,
    DateOnly? DueDate,
    Guid? AssignedEmployeeId,
    Guid? AssignedUserId,
    string? AssignedEmployeeName,
    Guid CreatedBy,
    Guid? CompletedBy,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string AssignedTo => AssignedEmployeeName
        ?? (AssignedEmployeeId.HasValue || AssignedUserId.HasValue ? "Unknown" : "Unassigned");
}
