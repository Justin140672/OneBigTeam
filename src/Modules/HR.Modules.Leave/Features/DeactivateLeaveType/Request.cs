namespace HR.Modules.Leave.Features.DeactivateLeaveType;

internal sealed record DeactivateLeaveTypeRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }

    internal Guid? ActorEmployeeId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
