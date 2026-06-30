namespace HR.Modules.Assets.Features.GetAsset;

internal sealed record GetAssetResponse(
    Guid Id,
    Guid CompanyId,
    string AssetNumber,
    Guid CategoryId,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    DateOnly? PurchaseDate,
    decimal? PurchasePrice,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
