namespace HR.Modules.Tasks.Features.GetEmployeeTasks;

internal sealed record GetEmployeeTasksResponse(IReadOnlyList<EmployeeTaskItem> Items);

internal sealed record EmployeeTaskItem(
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
    DateTimeOffset UpdatedAt);
