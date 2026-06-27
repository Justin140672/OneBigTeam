namespace HR.Modules.Tasks.Features.GetMyTasks;

internal sealed record GetMyTasksResponse(IReadOnlyList<TaskListItem> Items);

internal sealed record TaskListItem(
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
    DateTimeOffset UpdatedAt);
