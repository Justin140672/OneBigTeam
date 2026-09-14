namespace HR.Modules.Leave.Features.AwardToil;

internal sealed record AwardToilRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid AwardedByEmployeeId { get; init; }
    public decimal Days { get; init; }
    public DateOnly OccurredOn { get; init; }
    public string? Notes { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
