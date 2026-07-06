namespace HR.Infrastructure.Abstractions;

public sealed record PositionProfileRequiredAssetItem(
    Guid Id,
    Guid AssetCategoryId,
    bool IsMandatory,
    int Quantity);
