using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

public sealed class CompanyProfileEditModel
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}

public sealed class CompanyAddressEditModel
{
    public string Type { get; set; } = string.Empty;
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public sealed class CompanySettingsEditModel
{
    public string? TimeZone { get; set; }
    public string? Locale { get; set; }
    public HashSet<string> WorkingWeek { get; set; } = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
    [Range(0.5, 24)]
    public decimal HoursPerDay { get; set; } = 7.5m;
    [Range(1, 12)]
    public int LeaveYearStartMonth { get; set; } = 1;
    [Range(0, 365)]
    public decimal DefaultHolidayAllowance { get; set; } = 25;
    [Range(0, 24)]
    public int ProbationMonths { get; set; } = 3;
    public bool ExcludePublicHolidaysFromLeave { get; set; } = true;
    public bool ExcludePublicHolidaysFromSickness { get; set; }
    public int? FitNoteRequiredAfterDays { get; set; }
    public int? ReturnToWorkRequiredAfterDays { get; set; }
}
