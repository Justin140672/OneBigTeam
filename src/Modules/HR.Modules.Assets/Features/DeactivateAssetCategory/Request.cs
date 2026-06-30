namespace HR.Modules.Assets.Features.DeactivateAssetCategory;

internal sealed record DeactivateAssetCategoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
