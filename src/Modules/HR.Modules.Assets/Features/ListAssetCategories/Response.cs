namespace HR.Modules.Assets.Features.ListAssetCategories;

internal sealed record ListAssetCategoriesResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
