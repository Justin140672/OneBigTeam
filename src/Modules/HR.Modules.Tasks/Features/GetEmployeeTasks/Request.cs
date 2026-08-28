namespace HR.Modules.Tasks.Features.GetEmployeeTasks;

internal sealed record GetEmployeeTasksRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }

    // Optional filter — matches TaskItemStatus enum value names (case-insensitive).
    public string? Status { get; init; }

    public string? Search { get; init; }

    // Matches TaskPriority enum value names (case-insensitive).
    public string? Priority { get; init; }

    public DateOnly? DueDateFrom { get; init; }
    public DateOnly? DueDateTo { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
