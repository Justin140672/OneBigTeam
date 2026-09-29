using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;


public record ListPublicHolidaysResponse(List<PublicHolidayListItemModel> Items);

public record PublicHolidayListItemModel(
    Guid Id,
    Guid CompanyId,
    DateOnly Date,
    string Name,
    string CountryCode,
    DateTimeOffset CreatedAt,
    // Ticket 2: optimistic-concurrency token surfaced by the list (no dedicated GetById endpoint).
    int Version = 0)
{
    public int Year => Date.Year;
}


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


public record UpdatePublicHolidayRequest(
    Guid CompanyId,
    Guid Id,
    DateOnly Date,
    string Name,
    string CountryCode,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdatePublicHolidayResponse(
    Guid Id,
    Guid CompanyId,
    DateOnly Date,
    string Name,
    string CountryCode,
    DateTimeOffset CreatedAt,
    int Version = 0);


public sealed class PublicHolidayEditModel : IHasVersion
{
    public int Version { get; set; }

    [Required(ErrorMessage = "Please select a date.")]
    public DateTime? Date { get; set; }
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    [Required(ErrorMessage = "Country code is required.")]
    public string CountryCode { get; set; } = string.Empty;
}
