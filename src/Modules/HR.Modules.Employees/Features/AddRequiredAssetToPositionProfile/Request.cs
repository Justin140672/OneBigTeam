namespace HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;

internal sealed record AddRequiredAssetRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
    public Guid AssetCategoryId { get; init; }
    public bool IsMandatory { get; init; }
    public int Quantity { get; init; } = 1;

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
