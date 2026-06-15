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
    Guid CreatedBy,
    Guid? CompletedBy,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string AssignedTo => (AssignedEmployeeId is null && AssignedUserId is null)
        ? "Unassigned"
        : AssignedEmployeeId?.ToString() ?? AssignedUserId!.Value.ToString();
}
