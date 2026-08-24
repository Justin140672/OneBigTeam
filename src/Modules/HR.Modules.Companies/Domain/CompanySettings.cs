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

    // Enforced range is 1-10: 1 allows no zero-padding at all (e.g. "1"), while 10 digits is
    // generous enough for any realistic company size without being an absurd column width.
    public int EmployeeNumberMinimumLength { get; private set; }

    public AssetNumberMode AssetNumberMode { get; private set; }
    public string? AssetNumberPrefix { get; private set; }
    public int NextAssetNumber { get; private set; }

    // Same 1-10 rationale as EmployeeNumberMinimumLength.
    public int AssetNumberMinimumLength { get; private set; }

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
            //
            // SICK-05: ReturnToWorkRequiredAfterDays is confirmed as 1 working day (not the 3
            // working days an earlier draft of the sickness spec described) — a lightweight
            // "was this a real absence, does it need a chat" check is meant to happen almost
            // immediately after any absence, not just longer ones. It is evaluated against
            // SicknessRecord.TotalDays, which is a *working-day* count (see SicknessCalculator) —
            // that is intentional and different from the calendar-day basis used for the fit-note
            // threshold (see FitNoteEvaluator's doc comment). Confirmed decision recorded in
            // specifications/product-specifications/00-current-product-decisions.md
            // ("Sickness management").
            FitNoteRequiredAfterDays = 7,
            ReturnToWorkRequiredAfterDays = 1,
            // SICK-04 defaults: 4+ absence spells in a rolling 12 months ("frequent"), a single
            // weekday recurring 3+ times in a rolling 12 months ("weekday pattern"), a single spell
            // of 28+ calendar days ("long absence" — UK long-term sickness convention).
            FrequentAbsenceCountThreshold = 4,
            FrequentAbsenceWindowDays = 365,
            LongAbsenceDayThreshold = 28,
            WeekdayPatternOccurrenceThreshold = 3,
            WeekdayPatternWindowDays = 365,
            // PROB-03 defaults: manager check-in at 30 days, HR review at 60 days, reserved third
            // checkpoint at 90 days (see doc comment on the properties above).
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
            // No prefix/suffix, auto-generated numbers zero-padded to 4 digits (e.g. "0001") — a
            // new company shouldn't need to configure a numbering scheme before it can add
            // employees.
            EmployeeNumberMode = EmployeeNumberMode.Automatic,
            EmployeeNumberPrefix = null,
            NextEmployeeNumber = 1,
            EmployeeNumberMinimumLength = 4,
            // Manual by default — unlike employee numbering, there is no pre-existing "always
            // automatic" behaviour to preserve for assets, so the same conservative default as
            // every other opt-in numbering scheme applies.
            AssetNumberMode = AssetNumberMode.Manual,
            AssetNumberPrefix = null,
            NextAssetNumber = 1,
            AssetNumberMinimumLength = 4,
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

    /// <summary>
    /// Updates the asset-numbering fields. Kept separate from <see cref="UpdateHrPolicy"/> so the
    /// Asset numbering setting can be authorized/audited independently, mirroring how employee
    /// numbering fields are grouped within HR policy but asset numbering is its own concern.
    /// </summary>
    /// <summary>
    /// PROB-03: updates the configured probation review checkpoint days. Kept separate from
    /// <see cref="UpdateHrPolicy"/> as its own concern, matching how asset numbering is split out
    /// via <see cref="UpdateAssetNumberSettings"/>. Not currently invoked by any feature handler
    /// (no UI wiring yet — see the doc comment on the properties) but available for that future
    /// follow-up, and exercised directly by unit tests.
    /// </summary>
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
    }
}
