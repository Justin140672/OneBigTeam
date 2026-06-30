namespace HR.Modules.Assets.Features.ListAssetCategories;

internal sealed record ListAssetCategoriesRequest
{
    public Guid CompanyId { get; init; }
}
