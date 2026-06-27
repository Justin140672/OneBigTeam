namespace HR.Modules.Tasks.Features.GetUnassignedTasks;

internal sealed record GetUnassignedTasksResponse(IReadOnlyList<UnassignedTaskItem> Items);

internal sealed record UnassignedTaskItem(
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
