namespace HR.Modules.Leave.Features.AwardToil;

internal sealed record AwardToilRequest(
    Guid CompanyId,
    Guid EmployeeId,
    decimal Days,
    DateOnly OccurredOn,
    string? Notes)
{
    // Derived from ICurrentUser by the endpoint and set before the handler runs. This is never
    // accepted from the client request (see Endpoint.cs line ~34 for the security override).
    // Ticket 4: ensure the authenticated caller is the actor, not a client-supplied value.
    internal Guid AwardedByEmployeeId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
