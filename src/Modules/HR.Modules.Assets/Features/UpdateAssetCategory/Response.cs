namespace HR.Modules.Assets.Features.UpdateAssetCategory;

internal sealed record UpdateAssetCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
