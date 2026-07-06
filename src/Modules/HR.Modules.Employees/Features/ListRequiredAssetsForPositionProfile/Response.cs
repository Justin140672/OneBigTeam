namespace HR.Modules.Employees.Features.ListRequiredAssetsForPositionProfile;

internal sealed record ListRequiredAssetsResponse(IReadOnlyList<RequiredAssetListItem> Items);

internal sealed record RequiredAssetListItem(
    Guid Id,
    Guid AssetCategoryId,
    string AssetCategoryName,
    bool IsMandatory,
    int Quantity);
