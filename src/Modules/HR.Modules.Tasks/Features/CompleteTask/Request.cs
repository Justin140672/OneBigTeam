namespace HR.Modules.Tasks.Features.CompleteTask;

internal sealed record CompleteTaskRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string? OutcomeDecision { get; init; }
    public string? OutcomeReason { get; init; }

    // Populated by the endpoint from the authenticated user's sub claim.
    internal Guid CompletedBy { get; init; }
}
