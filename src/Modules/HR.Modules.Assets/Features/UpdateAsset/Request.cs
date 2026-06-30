namespace HR.Modules.Assets.Features.UpdateAsset;

internal sealed record UpdateAssetRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string AssetNumber { get; init; } = string.Empty;
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchasePrice { get; init; }
}
