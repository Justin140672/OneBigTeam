namespace HR.Modules.Tasks.Features.CompleteTask;

internal sealed record CompleteTaskResponse(
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
    DateTimeOffset UpdatedAt);
