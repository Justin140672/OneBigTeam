using HR.Modules.Assets.Domain;

namespace HR.Modules.Assets.Features.ListAssets;

internal sealed record ListAssetsResponse(
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
    AssetStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
