namespace HR.Modules.Tasks.Features.CompleteTask;

internal sealed record CompleteTaskRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string? OutcomeDecision { get; init; }
    public string? OutcomeReason { get; init; }

    // Populated by the endpoint from the authenticated user's sub claim.
    internal Guid CompletedBy { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
