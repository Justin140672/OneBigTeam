using HR.Modules.Tasks.Domain;

namespace HR.Modules.Tasks.Features.CreateTask;

internal sealed record CreateTaskRequest
{
    public Guid CompanyId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public TaskPriority Priority { get; init; }
    public TaskSource Source { get; init; }
    public DateOnly? DueDate { get; init; }
    public Guid? AssignedEmployeeId { get; init; }
    public Guid? AssignedUserId { get; init; }

    // Populated by the endpoint from the authenticated user's sub claim — not bound from the request body.
    internal Guid CreatedBy { get; init; }
}
