using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Companies.Domain;

internal sealed class CompanySettings
{
    private CompanySettings() { }

    public Guid CompanyId { get; private set; }
    public string TimeZone { get; private set; } = string.Empty;
    public string Locale { get; private set; } = string.Empty;
    public WorkingDays WorkingDays { get; private set; }
    public decimal HoursPerDay { get; private set; }
    public int LeaveYearStartMonth { get; private set; }
    public decimal DefaultHolidayAllowance { get; private set; }
    public int ProbationMonths { get; private set; }
    public bool ExcludePublicHolidaysFromLeave { get; private set; }
    public bool ExcludePublicHolidaysFromSickness { get; private set; }
    public bool DisplaySalaryOnEmployeeProfile { get; private set; }
    public int? FitNoteRequiredAfterDays { get; private set; }
    public int? ReturnToWorkRequiredAfterDays { get; private set; }
    public string PostcodeRegex { get; private set; } = UkContactRegexDefaults.Postcode;
    public string TelephoneRegex { get; private set; } = UkContactRegexDefaults.Telephone;
    public string MobileRegex { get; private set; } = UkContactRegexDefaults.Mobile;

    // Duplicated here rather than referencing HR.Modules.Documents' AcknowledgementStatementDefaults
    // constant — Companies must not take a dependency on the Documents module (module boundary
    // rules forbid HR.Modules.Companies -> HR.Modules.Documents references). Documents module reads
    // this value indirectly via ICompanyAcknowledgementSettingsReader.
    public const string DefaultAcknowledgementStatementText = "I confirm that I have read and understood this document.";

    public string DefaultAcknowledgementStatement { get; private set; } = DefaultAcknowledgementStatementText;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CompanySettings CreateDefault(Guid companyId, DateTimeOffset now)
    {
        return new CompanySettings
        {
            CompanyId = companyId,
            TimeZone = "UTC",
            Locale = "en-GB",
            WorkingDays = WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                          WorkingDays.Thursday | WorkingDays.Friday,
            HoursPerDay = 7.5m,
            LeaveYearStartMonth = 1,
            DefaultHolidayAllowance = 25,
            ProbationMonths = 6,
            ExcludePublicHolidaysFromLeave = true,
            ExcludePublicHolidaysFromSickness = false,
            DisplaySalaryOnEmployeeProfile = false,
            FitNoteRequiredAfterDays = null,
            ReturnToWorkRequiredAfterDays = null,
            PostcodeRegex = UkContactRegexDefaults.Postcode,
            TelephoneRegex = UkContactRegexDefaults.Telephone,
            MobileRegex = UkContactRegexDefaults.Mobile,
            DefaultAcknowledgementStatement = DefaultAcknowledgementStatementText,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(
        string timeZone,
        string locale,
        WorkingDays workingDays,
        decimal hoursPerDay,
        int leaveYearStartMonth,
        decimal defaultHolidayAllowance,
        int probationMonths,
        bool excludePublicHolidaysFromLeave,
        bool excludePublicHolidaysFromSickness,
        bool displaySalaryOnEmployeeProfile,
        int? fitNoteRequiredAfterDays,
        int? returnToWorkRequiredAfterDays,
        string defaultAcknowledgementStatement,
        DateTimeOffset now)
    {
        TimeZone = timeZone;
        Locale = locale;
        WorkingDays = workingDays;
        HoursPerDay = hoursPerDay;
        LeaveYearStartMonth = leaveYearStartMonth;
        DefaultHolidayAllowance = defaultHolidayAllowance;
        ProbationMonths = probationMonths;
        ExcludePublicHolidaysFromLeave = excludePublicHolidaysFromLeave;
        ExcludePublicHolidaysFromSickness = excludePublicHolidaysFromSickness;
        DisplaySalaryOnEmployeeProfile = displaySalaryOnEmployeeProfile;
        FitNoteRequiredAfterDays = fitNoteRequiredAfterDays;
        ReturnToWorkRequiredAfterDays = returnToWorkRequiredAfterDays;
        DefaultAcknowledgementStatement = string.IsNullOrWhiteSpace(defaultAcknowledgementStatement)
            ? DefaultAcknowledgementStatementText
            : defaultAcknowledgementStatement;
        UpdatedAt = now;
    }
}
