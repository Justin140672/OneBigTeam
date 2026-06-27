namespace HR.Modules.Tasks.Features.GetTeamTasks;

internal sealed record GetTeamTasksResponse(IReadOnlyList<TeamTaskItem> Items);

internal sealed record TeamTaskItem(
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
