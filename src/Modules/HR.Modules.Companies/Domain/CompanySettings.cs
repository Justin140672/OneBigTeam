using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Contracts;
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
    public int FitNoteRequiredAfterDays { get; private set; }
    public int ReturnToWorkRequiredAfterDays { get; private set; }
    public string PostcodeRegex { get; private set; } = UkContactRegexDefaults.Postcode;
    public string TelephoneRegex { get; private set; } = UkContactRegexDefaults.Telephone;
    public string MobileRegex { get; private set; } = UkContactRegexDefaults.Mobile;

    // Duplicated here rather than referencing HR.Modules.Documents' AcknowledgementStatementDefaults
    // constant — Companies must not take a dependency on the Documents module (module boundary
    // rules forbid HR.Modules.Companies -> HR.Modules.Documents references). Documents module reads
    // this value indirectly via ICompanyAcknowledgementSettingsReader.
    public const string DefaultAcknowledgementStatementText = "I confirm that I have read and understood this document.";

    public string DefaultAcknowledgementStatement { get; private set; } = DefaultAcknowledgementStatementText;
    public int AcknowledgementReminderIntervalDays { get; private set; } = 3;
    public NoticePeriodUnit NoticePeriodUnit { get; private set; }
    public int NoticePeriodLength { get; private set; }
    public bool AutoDisableAccessOnLeavingDate { get; private set; }

    public EmployeeNumberMode EmployeeNumberMode { get; private set; }
    public string? EmployeeNumberPrefix { get; private set; }
    public int NextEmployeeNumber { get; private set; }

    // Enforced range is 1-10: 1 allows no zero-padding at all (e.g. "1"), while 10 digits is
    // generous enough for any realistic company size without being an absurd column width.
    public int EmployeeNumberMinimumLength { get; private set; }

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
            // Mandatory, no opt-out — every company requires fit-note evidence after a week of
            // sickness and a return-to-work review after 1 day by default.
            FitNoteRequiredAfterDays = 7,
            ReturnToWorkRequiredAfterDays = 1,
            PostcodeRegex = UkContactRegexDefaults.Postcode,
            TelephoneRegex = UkContactRegexDefaults.Telephone,
            MobileRegex = UkContactRegexDefaults.Mobile,
            DefaultAcknowledgementStatement = DefaultAcknowledgementStatementText,
            AcknowledgementReminderIntervalDays = 3,
            NoticePeriodUnit = NoticePeriodUnit.Months,
            NoticePeriodLength = 1,
            AutoDisableAccessOnLeavingDate = true,
            // No prefix/suffix, auto-generated numbers zero-padded to 4 digits (e.g. "0001") — a
            // new company shouldn't need to configure a numbering scheme before it can add
            // employees.
            EmployeeNumberMode = EmployeeNumberMode.Automatic,
            EmployeeNumberPrefix = null,
            NextEmployeeNumber = 1,
            EmployeeNumberMinimumLength = 4,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Updates the company-profile-scoped fields (Company Administrator territory).
    /// HR-policy fields are updated separately via <see cref="UpdateHrPolicy"/> so the two
    /// concerns can be authorized and audited independently against the same aggregate.
    /// </summary>
    public void UpdateCompanyProfile(
        string timeZone,
        string locale,
        DateTimeOffset now)
    {
        TimeZone = timeZone;
        Locale = locale;
        UpdatedAt = now;
    }

    /// <summary>
    /// Updates the HR-policy fields (HR Administrator territory). See
    /// <see cref="UpdateCompanyProfile"/> for the company-profile counterpart.
    /// </summary>
    public void UpdateHrPolicy(
        WorkingDays workingDays,
        decimal hoursPerDay,
        int leaveYearStartMonth,
        decimal defaultHolidayAllowance,
        int probationMonths,
        bool excludePublicHolidaysFromLeave,
        bool excludePublicHolidaysFromSickness,
        bool displaySalaryOnEmployeeProfile,
        int fitNoteRequiredAfterDays,
        int returnToWorkRequiredAfterDays,
        string defaultAcknowledgementStatement,
        int acknowledgementReminderIntervalDays,
        NoticePeriodUnit noticePeriodUnit,
        int noticePeriodLength,
        bool autoDisableAccessOnLeavingDate,
        EmployeeNumberMode employeeNumberMode,
        string? employeeNumberPrefix,
        int nextEmployeeNumber,
        int employeeNumberMinimumLength,
        DateTimeOffset now)
    {
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
        AcknowledgementReminderIntervalDays = acknowledgementReminderIntervalDays;
        NoticePeriodUnit = noticePeriodUnit;
        NoticePeriodLength = noticePeriodLength;
        AutoDisableAccessOnLeavingDate = autoDisableAccessOnLeavingDate;
        EmployeeNumberMode = employeeNumberMode;
        EmployeeNumberPrefix = string.IsNullOrWhiteSpace(employeeNumberPrefix) ? null : employeeNumberPrefix.Trim();
        NextEmployeeNumber = nextEmployeeNumber;
        EmployeeNumberMinimumLength = employeeNumberMinimumLength;
        UpdatedAt = now;
    }
}
