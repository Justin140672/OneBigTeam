using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListPublicHolidaysResponse(List<PublicHolidayListItemModel> Items);

public record PublicHolidayListItemModel(
    Guid Id,
    Guid CompanyId,
    DateOnly Date,
    string Name,
    string CountryCode,
    DateTimeOffset CreatedAt)
{
    public int Year => Date.Year;
}

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreatePublicHolidayRequest(
    Guid CompanyId,
    DateOnly Date,
    string Name,
    string CountryCode);

public record CreatePublicHolidayResponse(
    Guid Id,
    Guid CompanyId,
    DateOnly Date,
    string Name,
    string CountryCode,
    DateTimeOffset CreatedAt);

// ── UPDATE ────────────────────────────────────────────────────────────────────

public record UpdatePublicHolidayRequest(
    Guid CompanyId,
    Guid Id,
    DateOnly Date,
    string Name,
    string CountryCode);

public record UpdatePublicHolidayResponse(
    Guid Id,
    Guid CompanyId,
    DateOnly Date,
    string Name,
    string CountryCode,
    DateTimeOffset CreatedAt);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class PublicHolidayEditModel
{
    [Required(ErrorMessage = "Please select a date.")]
    public DateTime? Date { get; set; }
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    [Required(ErrorMessage = "Country code is required.")]
    public string CountryCode { get; set; } = string.Empty;
}
