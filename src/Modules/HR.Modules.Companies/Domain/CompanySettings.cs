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

    // SICK-04: configurable attendance-pattern alert thresholds. Mandatory, no opt-out (mirrors
    // FitNoteRequiredAfterDays/ReturnToWorkRequiredAfterDays) — every company gets informational
    // attendance alerts by default, tuned to sensible UK-typical values. Not yet exposed through
    // UpdateHrPolicy/the HR settings UI — this ticket establishes the persisted, per-company
    // configurable home for the thresholds (satisfying "configurable rules"); wiring an editable
    // UI is a reasonable, deliberately deferred follow-up rather than something this ticket
    // requires.
    public int FrequentAbsenceCountThreshold { get; private set; }
    public int FrequentAbsenceWindowDays { get; private set; }
    public int LongAbsenceDayThreshold { get; private set; }
    public int WeekdayPatternOccurrenceThreshold { get; private set; }
    public int WeekdayPatternWindowDays { get; private set; }

    // PROB-03: configurable probation review checkpoint days (offsets in days from the probation
    // start date). Stored as 3 nullable int columns rather than a delimited/JSON column — the
    // schedule is always exactly "up to 3 checkpoints" (documented mapping: the first surviving
    // checkpoint is the manager check-in, the second is the HR review; the final decision review
    // is always scheduled separately at the expected end date, never one of these checkpoints —
    // see ProbationReviewScheduler), so a fixed small set of nullable columns is simpler than a
    // structured column and still lets a company disable a checkpoint (set it to null) or tune the
    // day offsets. Not yet exposed through UpdateHrPolicy/the HR settings UI — mirrors the
    // FrequentAbsenceCountThreshold precedent above: this establishes the persisted, per-company
    // configurable home for the schedule; wiring an editable UI is a deliberately deferred
    // follow-up rather than something PROB-03 requires.
    public int? ProbationCheckpointDay1 { get; private set; }
    public int? ProbationCheckpointDay2 { get; private set; }
    public int? ProbationCheckpointDay3 { get; private set; }
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

    public int EmployeeNumberMinimumLength { get; private set; }

    public AssetNumberMode AssetNumberMode { get; private set; }
    public string? AssetNumberPrefix { get; private set; }
    public int NextAssetNumber { get; private set; }

    public int AssetNumberMinimumLength { get; private set; }

    public bool VacancyApprovalRequired { get; private set; }
    public bool OfferApprovalRequired { get; private set; }
    public int CandidateRetentionDays { get; private set; }

    public bool EmailNotificationsEnabled { get; private set; }
    public bool ScheduledRemindersEnabled { get; private set; }

    public bool DocumentRemindersEnabled { get; private set; }
    public int? DocumentReminderOffsetDays1 { get; private set; }
    public int? DocumentReminderOffsetDays2 { get; private set; }
    public int? DocumentReminderOffsetDays3 { get; private set; }

    // The primary domain and naming convention are mandatory once set: UpdateWorkEmailSettings rejects a
    // missing/invalid domain, so the domain can never be cleared. It is null only on a freshly
    // created default row (before provisioning assigns one) and on legacy rows with no derivable domain.
    public bool WorkEmailSuggestionsEnabled { get; private set; }
    public string? WorkEmailPrimaryDomain { get; private set; }
    public string[] WorkEmailAdditionalDomains { get; private set; } = [];
    public WorkEmailNamingConvention WorkEmailNamingConvention { get; private set; } = WorkEmailNamingConvention.FirstNameDotLastName;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

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
            FitNoteRequiredAfterDays = 7,
            ReturnToWorkRequiredAfterDays = 1,
            FrequentAbsenceCountThreshold = 4,
            FrequentAbsenceWindowDays = 365,
            LongAbsenceDayThreshold = 28,
            WeekdayPatternOccurrenceThreshold = 3,
            WeekdayPatternWindowDays = 365,
            ProbationCheckpointDay1 = 30,
            ProbationCheckpointDay2 = 60,
            ProbationCheckpointDay3 = 90,
            PostcodeRegex = UkContactRegexDefaults.Postcode,
            TelephoneRegex = UkContactRegexDefaults.Telephone,
            MobileRegex = UkContactRegexDefaults.Mobile,
            DefaultAcknowledgementStatement = DefaultAcknowledgementStatementText,
            AcknowledgementReminderIntervalDays = 3,
            NoticePeriodUnit = NoticePeriodUnit.Months,
            NoticePeriodLength = 1,
            AutoDisableAccessOnLeavingDate = true,
            EmployeeNumberMode = EmployeeNumberMode.Automatic,
            EmployeeNumberPrefix = null,
            NextEmployeeNumber = 1,
            EmployeeNumberMinimumLength = 4,
            AssetNumberMode = AssetNumberMode.Manual,
            AssetNumberPrefix = null,
            NextAssetNumber = 1,
            AssetNumberMinimumLength = 4,
            VacancyApprovalRequired = false,
            OfferApprovalRequired = false,
            CandidateRetentionDays = 730,
            EmailNotificationsEnabled = true,
            ScheduledRemindersEnabled = true,
            DocumentRemindersEnabled = true,
            DocumentReminderOffsetDays1 = 90,
            DocumentReminderOffsetDays2 = 30,
            DocumentReminderOffsetDays3 = 7,
            WorkEmailSuggestionsEnabled = true,
            WorkEmailPrimaryDomain = null,
            WorkEmailAdditionalDomains = [],
            WorkEmailNamingConvention = WorkEmailNamingConvention.FirstNameDotLastName,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1,
        };
    }

    public void UpdateCompanyProfile(
        string timeZone,
        string locale,
        DateTimeOffset now)
    {
        TimeZone = timeZone;
        Locale = locale;
        UpdatedAt = now;
        Version++;
    }

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
        Version++;
    }

    public void UpdateProbationCheckpoints(
        int? checkpointDay1,
        int? checkpointDay2,
        int? checkpointDay3,
        DateTimeOffset now)
    {
        ProbationCheckpointDay1 = checkpointDay1;
        ProbationCheckpointDay2 = checkpointDay2;
        ProbationCheckpointDay3 = checkpointDay3;
        UpdatedAt = now;
        Version++;
    }

    public void UpdateAttendanceAlertThresholds(
        int frequentAbsenceCountThreshold,
        int frequentAbsenceWindowDays,
        int longAbsenceDayThreshold,
        int weekdayPatternOccurrenceThreshold,
        int weekdayPatternWindowDays,
        DateTimeOffset now)
    {
        FrequentAbsenceCountThreshold = frequentAbsenceCountThreshold;
        FrequentAbsenceWindowDays = frequentAbsenceWindowDays;
        LongAbsenceDayThreshold = longAbsenceDayThreshold;
        WeekdayPatternOccurrenceThreshold = weekdayPatternOccurrenceThreshold;
        WeekdayPatternWindowDays = weekdayPatternWindowDays;
        UpdatedAt = now;
        Version++;
    }

    public void UpdateRecruitmentSettings(
        bool vacancyApprovalRequired,
        bool offerApprovalRequired,
        int candidateRetentionDays,
        DateTimeOffset now)
    {
        VacancyApprovalRequired = vacancyApprovalRequired;
        OfferApprovalRequired = offerApprovalRequired;
        CandidateRetentionDays = candidateRetentionDays;
        UpdatedAt = now;
        Version++;
    }

    public void UpdateNotificationSettings(
        bool emailNotificationsEnabled,
        bool scheduledRemindersEnabled,
        DateTimeOffset now)
    {
        EmailNotificationsEnabled = emailNotificationsEnabled;
        ScheduledRemindersEnabled = scheduledRemindersEnabled;
        UpdatedAt = now;
        Version++;
    }

    public void UpdateDocumentReminderSettings(
        bool remindersEnabled,
        int? offsetDays1,
        int? offsetDays2,
        int? offsetDays3,
        DateTimeOffset now)
    {
        DocumentRemindersEnabled = remindersEnabled;
        DocumentReminderOffsetDays1 = offsetDays1;
        DocumentReminderOffsetDays2 = offsetDays2;
        DocumentReminderOffsetDays3 = offsetDays3;
        UpdatedAt = now;
        Version++;
    }

    public void UpdateWorkEmailSettings(
        bool suggestionsEnabled,
        string primaryDomain,
        IEnumerable<string>? additionalDomains,
        WorkEmailNamingConvention namingConvention,
        DateTimeOffset now)
    {
        var primary = WorkEmailAddressBuilder.NormalizeDomain(primaryDomain);
        if (!WorkEmailAddressBuilder.IsValidDomain(primary))
            throw new ArgumentException("A valid primary work email domain is required.", nameof(primaryDomain));

        if (!Enum.IsDefined(namingConvention))
            throw new ArgumentOutOfRangeException(nameof(namingConvention));

        WorkEmailSuggestionsEnabled = suggestionsEnabled;
        WorkEmailPrimaryDomain = primary;
        WorkEmailAdditionalDomains = (additionalDomains ?? [])
            .Select(WorkEmailAddressBuilder.NormalizeDomain)
            .OfType<string>()
            .Where(domain => domain != primary)
            .Distinct()
            .ToArray();
        WorkEmailNamingConvention = namingConvention;
        UpdatedAt = now;
        Version++;
    }

    public void UpdateAssetNumberSettings(
        AssetNumberMode assetNumberMode,
        string? assetNumberPrefix,
        int nextAssetNumber,
        int assetNumberMinimumLength,
        DateTimeOffset now)
    {
        AssetNumberMode = assetNumberMode;
        AssetNumberPrefix = string.IsNullOrWhiteSpace(assetNumberPrefix) ? null : assetNumberPrefix.Trim();
        NextAssetNumber = nextAssetNumber;
        AssetNumberMinimumLength = assetNumberMinimumLength;
        UpdatedAt = now;
        Version++;
    }
}
