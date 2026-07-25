using System.ComponentModel.DataAnnotations;
using HR.Infrastructure.Abstractions;

namespace HR.Web.Models;

// Name and Addresses are saved together via a single UpdateCompanyRequest, so they share one
// model/EditContext (owned by CompanyEdit) even though they're shown on separate tabs.
public sealed class CompanyDetailsEditModel
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    public List<CompanyAddressEditModel> Addresses { get; set; } = [];
}

public sealed class CompanyAddressEditModel
{
    public string Type { get; set; } = string.Empty;
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    private string? _postalCode;
    [DynamicRegex(nameof(PostcodeRegexPattern), ErrorMessage = "Enter a valid postcode.")]
    public string? PostalCode
    {
        get => _postalCode;
        set => _postalCode = value?.ToUpperInvariant();
    }
    public string? CountryCode { get; set; }

    public string? PostcodeRegexPattern { get; set; }
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
    public bool DisplaySalaryOnEmployeeProfile { get; set; }
    public int? FitNoteRequiredAfterDays { get; set; }
    public int? ReturnToWorkRequiredAfterDays { get; set; }
    [MaxLength(2000)]
    public string? DefaultAcknowledgementStatement { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Reminder interval must be at least 1 day.")]
    public int AcknowledgementReminderIntervalDays { get; set; } = 3;
    public NoticePeriodUnit NoticePeriodUnit { get; set; } = NoticePeriodUnit.Months;
    [Range(1, int.MaxValue, ErrorMessage = "Notice period length must be greater than 0.")]
    public int NoticePeriodLength { get; set; } = 1;
    public bool AutoDisableAccessOnLeavingDate { get; set; } = true;
}
