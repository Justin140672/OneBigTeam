namespace HR.Modules.Assets.Features.CreateAssetCategory;

internal sealed record CreateAssetCategoryRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
