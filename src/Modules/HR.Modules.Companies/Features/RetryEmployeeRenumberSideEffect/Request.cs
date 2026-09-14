namespace HR.Modules.Companies.Features.RetryEmployeeRenumberSideEffect;

internal sealed record RetryEmployeeRenumberSideEffectRequest
{
    public Guid CompanyId { get; init; }
    public Guid OutboxMessageId { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
