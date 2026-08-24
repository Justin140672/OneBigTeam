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
}
