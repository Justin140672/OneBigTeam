namespace HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;

internal sealed record AddRequiredAssetResponse(
    Guid Id,
    Guid PositionProfileId,
    Guid AssetCategoryId,
    bool IsMandatory,
    int Quantity);
