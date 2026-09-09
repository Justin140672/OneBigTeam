using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;

public record ListAssetCategoriesResponse(List<AssetCategoryListItemModel> Items);

public record AssetCategoryListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version = 0);

public record CreateAssetCategoryRequest(Guid CompanyId, string Name, string? Description);

public record CreateAssetCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateAssetCategoryRequest(
    Guid CompanyId, Guid Id, string Name, string? Description,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdateAssetCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version = 0);

public sealed class AssetCategoryEditModel : IHasVersion
{
    public int Version { get; set; }
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
