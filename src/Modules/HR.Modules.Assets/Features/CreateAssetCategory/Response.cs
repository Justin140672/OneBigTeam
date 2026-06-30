namespace HR.Modules.Assets.Features.CreateAssetCategory;

internal sealed record CreateAssetCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
