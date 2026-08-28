namespace HR.Modules.Tasks.Features.GetMyTasks;

internal sealed record GetMyTasksRequest
{
    public Guid CompanyId { get; init; }

    // Optional filter — matches TaskItemStatus enum value names (case-insensitive).
    public string? Status { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    // Populated by the endpoint from the authenticated user's sub claim.
    internal Guid UserId { get; init; }
}
