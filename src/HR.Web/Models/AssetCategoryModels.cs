using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

public record ListAssetCategoriesResponse(List<AssetCategoryListItemModel> Items);

public record AssetCategoryListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateAssetCategoryRequest(Guid CompanyId, string Name, string? Description);

public record CreateAssetCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateAssetCategoryRequest(Guid CompanyId, Guid Id, string Name, string? Description);

public record UpdateAssetCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class AssetCategoryEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
