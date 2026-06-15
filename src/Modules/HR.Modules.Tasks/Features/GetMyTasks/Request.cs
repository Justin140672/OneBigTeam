namespace HR.Modules.Tasks.Features.GetMyTasks;

internal sealed record GetMyTasksRequest
{
    public Guid CompanyId { get; init; }

    // Optional filter — matches TaskItemStatus enum value names (case-insensitive).
    public string? Status { get; init; }

    // Populated by the endpoint from the authenticated user's sub claim.
    internal Guid UserId { get; init; }
}
