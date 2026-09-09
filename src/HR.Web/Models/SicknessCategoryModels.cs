using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;

public record ListSicknessCategoriesResponse(List<SicknessCategoryListItemModel> Items);

public record SicknessCategoryListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version = 0);

public record CreateSicknessCategoryRequest(Guid CompanyId, string Name, int DisplayOrder);

public record CreateSicknessCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateSicknessCategoryRequest(
    Guid CompanyId, Guid Id, string Name, int DisplayOrder,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdateSicknessCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version = 0);

public sealed class SicknessCategoryEditModel : IHasVersion
{
    public int Version { get; set; }
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    [Range(0, int.MaxValue, ErrorMessage = "Display order cannot be negative.")]
    public int DisplayOrder { get; set; }
}
