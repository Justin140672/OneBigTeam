namespace HR.Modules.Recruitment.Features.RespondToOffer;

internal sealed record RespondToOfferRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }

    // "Accepted" | "Declined" | "Withdrawn". "AwaitingResponse" is not a valid target here — it is
    // the state an offer starts in when made. Case-insensitive.
    public string Status { get; init; } = string.Empty;

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
