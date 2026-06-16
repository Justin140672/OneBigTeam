namespace HR.Web.Models;

public sealed record TaskListResponse(IReadOnlyList<TaskListItem> Items);

public sealed record UnassignedTaskListResponse(IReadOnlyList<UnassignedTaskItem> Items);

public sealed record UnassignedTaskItem(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    string Source,
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
