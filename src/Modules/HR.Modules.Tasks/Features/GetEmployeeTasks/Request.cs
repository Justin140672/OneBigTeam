namespace HR.Modules.Tasks.Features.GetEmployeeTasks;

internal sealed record GetEmployeeTasksRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }

    // Optional filter — matches TaskItemStatus enum value names (case-insensitive).
    public string? Status { get; init; }
}
