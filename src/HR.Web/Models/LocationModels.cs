using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListLocationsResponse(List<LocationListItemModel> Items);

public record LocationListItemModel(
    Guid Id,
    string Name,
    Guid LocationTypeId,
    bool IsActive);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetLocationResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid LocationTypeId,
    bool IsActive);

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreateLocationRequest(
    Guid CompanyId,
    string Name,
    string? Description,
    Guid LocationTypeId);

public record CreateLocationResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid LocationTypeId,
    bool IsActive,
    DateTimeOffset CreatedAt);

// ── UPDATE ────────────────────────────────────────────────────────────────────

public record UpdateLocationRequest(
    Guid CompanyId,
    Guid Id,
    string Name,
    string? Description,
    Guid LocationTypeId);

public record UpdateLocationResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid LocationTypeId,
    bool IsActive,
    DateTimeOffset UpdatedAt);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class LocationEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid LocationTypeId { get; set; }
}
