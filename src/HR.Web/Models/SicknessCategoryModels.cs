namespace HR.Web.Models;

public record ListSicknessCategoriesResponse(List<SicknessCategoryListItemModel> Items);

public record SicknessCategoryListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateSicknessCategoryRequest(Guid CompanyId, string Name, int DisplayOrder);

public record CreateSicknessCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateSicknessCategoryRequest(Guid CompanyId, Guid Id, string Name, int DisplayOrder);

public record UpdateSicknessCategoryResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class SicknessCategoryEditModel
{
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
