namespace HR.Modules.Leave.Features.CreateLeavePolicy;

internal sealed record CreateLeavePolicyRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int CarryOverDays { get; init; }
    public bool AllowNegativeBalance { get; init; }
    public bool IsDefault { get; init; }

    // LEAVE-07: defaults to true (the safer choice) when a caller does not specify it.
    public bool RequiresApproval { get; init; } = true;

    // Populated by the endpoint from the authenticated user's "sub" claim — never bound from the
    // client body (internal properties are not touched by FastEndpoints' JSON model binding).
    internal Guid? ActorEmployeeId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
