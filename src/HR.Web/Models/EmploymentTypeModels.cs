using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

public record ListEmploymentTypesResponse(List<EmploymentTypeListItemModel> Items);

public record EmploymentTypeListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateEmploymentTypeRequest(Guid CompanyId, string Name, string? Description);

public record CreateEmploymentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateEmploymentTypeRequest(Guid CompanyId, Guid Id, string Name, string? Description);

public record UpdateEmploymentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset UpdatedAt);

public sealed class EmploymentTypeEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
