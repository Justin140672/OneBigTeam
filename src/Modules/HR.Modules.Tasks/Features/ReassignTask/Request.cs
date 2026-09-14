namespace HR.Modules.Tasks.Features.ReassignTask;

internal sealed record ReassignTaskRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid? AssignedEmployeeId { get; init; }
    public Guid? AssignedUserId { get; init; }

    // Populated by the endpoint from the authenticated user's sub claim.
    internal Guid? ActorUserId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
