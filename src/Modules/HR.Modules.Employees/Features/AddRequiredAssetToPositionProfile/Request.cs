namespace HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;

internal sealed record AddRequiredAssetRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
    public Guid AssetCategoryId { get; init; }
    public bool IsMandatory { get; init; }
    public int Quantity { get; init; } = 1;
}
