namespace HR.Modules.Assets.Features.CreateAsset;

internal sealed record CreateAssetRequest
{
    public Guid CompanyId { get; init; }
    // Null/blank when the company is in Automatic asset-numbering mode: the handler generates the
    // number itself via IAssetNumberGenerator in that case. Required in Manual mode.
    public string? AssetNumber { get; init; }
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchasePrice { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }

    // Populated by the endpoint from the authenticated user's id (ticket 3, P1 follow-up item 3) -
    // the actor scope an idempotency key is bound to. Never bound from the client body. Null when
    // there is no authenticated user (e.g. a system-initiated call), in which case the audit event
    // records no actor rather than a misleading Guid.Empty.
    internal Guid? ActorId { get; init; }
}
