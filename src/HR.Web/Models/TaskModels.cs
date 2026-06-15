namespace HR.Web.Models;

public sealed record TaskListResponse(IReadOnlyList<TaskListItem> Items);

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
