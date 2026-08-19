namespace HR.Modules.Employees.Contracts;

public sealed record PositionProfileRequiredAssetItem(
    Guid Id,
    Guid AssetCategoryId,
    bool IsMandatory,
    int Quantity);
