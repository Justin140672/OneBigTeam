namespace HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

internal sealed record AssignLeavePolicyToEmployeeRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeavePolicyId { get; init; }
    public DateOnly EffectiveFrom { get; init; }

    // Populated by the endpoint from the authenticated user's "sub" claim — never bound from the
    // client body (internal properties are not touched by FastEndpoints' JSON model binding).
    internal Guid? ActorEmployeeId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
