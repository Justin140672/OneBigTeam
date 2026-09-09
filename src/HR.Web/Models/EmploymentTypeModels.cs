using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;

public record ListEmploymentTypesResponse(List<EmploymentTypeListItemModel> Items);

public record EmploymentTypeListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version = 0);

public record CreateEmploymentTypeRequest(Guid CompanyId, string Name, string? Description);

public record CreateEmploymentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateEmploymentTypeRequest(
    Guid CompanyId, Guid Id, string Name, string? Description,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdateEmploymentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    int Version = 0);

public sealed class EmploymentTypeEditModel : IHasVersion
{
    public int Version { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
