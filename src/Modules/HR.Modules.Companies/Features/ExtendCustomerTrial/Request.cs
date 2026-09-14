namespace HR.Modules.Companies.Features.ExtendCustomerTrial;

internal sealed record ExtendCustomerTrialRequest
{
    public Guid CompanyId { get; init; }
    public DateTimeOffset NewTrialExpiresAt { get; init; }
    public string Reason { get; init; } = string.Empty;

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
