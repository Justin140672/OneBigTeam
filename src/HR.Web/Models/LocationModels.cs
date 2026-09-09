using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListLocationsResponse(List<LocationListItemModel> Items);

public record LocationListItemModel(
    Guid Id,
    string Name,
    Guid LocationTypeId,
    bool IsActive,
    int Version = 0);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetLocationResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid LocationTypeId,
    bool IsActive,
    int Version = 0);

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
    Guid LocationTypeId,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdateLocationResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid LocationTypeId,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    int Version = 0);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class LocationEditModel : IHasVersion
{
    public int Version { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    [Required(ErrorMessage = "Location type is required.")]
    public Guid? LocationTypeId { get; set; }
}
