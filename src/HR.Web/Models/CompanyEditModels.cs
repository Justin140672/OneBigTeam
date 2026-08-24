using System.ComponentModel.DataAnnotations;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

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

    [Required(ErrorMessage = "Line 1 is required.")]
    [MaxLength(200)]
    public string? Line1 { get; set; }

    [MaxLength(200)]
    public string? Line2 { get; set; }

    [Required(ErrorMessage = "City is required.")]
    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Region { get; set; }

    private string? _postalCode;
    [DynamicRegex(nameof(PostcodeRegexPattern), ErrorMessage = "Enter a valid postcode.")]
    [MaxLength(20)]
    public string? PostalCode
    {
        get => _postalCode;
        set => _postalCode = value?.ToUpperInvariant();
    }

    [Required(ErrorMessage = "Country code is required.")]
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Enter a valid 2-letter country code.")]
    public string? CountryCode { get; set; }

    public string? PostcodeRegexPattern { get; set; }
}

// Company Administrator territory — regional display settings only. HR-policy fields live in
// HrSettingsEditModel (see HrSettingsPage), reachable only to HR Administrators.
public sealed class CompanySettingsEditModel
{
    public string? TimeZone { get; set; }
    public string? Locale { get; set; }
}

// HR Administrator territory — HR-policy fields moved out of CompanySettingsEditModel.
public sealed class HrSettingsEditModel
{
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
    [Range(1, int.MaxValue, ErrorMessage = "Fit note required after must be at least 1 day.")]
    public int FitNoteRequiredAfterDays { get; set; } = 7;
    [Range(1, int.MaxValue, ErrorMessage = "Return-to-work review required after must be at least 1 day.")]
    public int ReturnToWorkRequiredAfterDays { get; set; } = 1;
    [MaxLength(2000)]
    public string? DefaultAcknowledgementStatement { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Reminder interval must be at least 1 day.")]
    public int AcknowledgementReminderIntervalDays { get; set; } = 3;
    public NoticePeriodUnit NoticePeriodUnit { get; set; } = NoticePeriodUnit.Months;
    [Range(1, int.MaxValue, ErrorMessage = "Notice period length must be greater than 0.")]
    public int NoticePeriodLength { get; set; } = 1;
    public bool AutoDisableAccessOnLeavingDate { get; set; } = true;
    public EmployeeNumberMode EmployeeNumberMode { get; set; } = EmployeeNumberMode.Manual;
    [MaxLength(20)]
    public string? EmployeeNumberPrefix { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Next employee number must be greater than 0.")]
    public int NextEmployeeNumber { get; set; } = 1;
    [Range(1, 10, ErrorMessage = "Minimum numeric length must be between 1 and 10.")]
    public int EmployeeNumberMinimumLength { get; set; } = 1;

    public AssetNumberMode AssetNumberMode { get; set; } = AssetNumberMode.Manual;
    [MaxLength(20)]
    public string? AssetNumberPrefix { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Next asset number must be greater than 0.")]
    public int NextAssetNumber { get; set; } = 1;
    [Range(1, 10, ErrorMessage = "Minimum numeric length must be between 1 and 10.")]
    public int AssetNumberMinimumLength { get; set; } = 1;

    // Baselines captured on load, used purely to detect a format change (prefix/minimum length)
    // to Employee/Asset numbering while staying in Automatic mode, so the page can warn the user
    // before save that this will trigger an automatic renumber of existing records.
    public EmployeeNumberMode EmployeeNumberOriginalMode { get; set; }
    public string? EmployeeNumberOriginalPrefix { get; set; }
    public int EmployeeNumberOriginalMinimumLength { get; set; }
    public AssetNumberMode AssetNumberOriginalMode { get; set; }
    public string? AssetNumberOriginalPrefix { get; set; }
    public int AssetNumberOriginalMinimumLength { get; set; }
}
