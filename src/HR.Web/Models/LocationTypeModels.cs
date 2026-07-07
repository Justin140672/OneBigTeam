using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

public record ListLocationTypesResponse(List<LocationTypeListItemModel> Items);

public record LocationTypeListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateLocationTypeRequest(Guid CompanyId, string Name, string? Description);

public record CreateLocationTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateLocationTypeRequest(Guid CompanyId, Guid Id, string Name, string? Description);

public record UpdateLocationTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset UpdatedAt);

public sealed class LocationTypeEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
