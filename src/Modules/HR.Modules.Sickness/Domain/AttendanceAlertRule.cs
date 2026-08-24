namespace HR.Modules.Sickness.Domain;

/// <summary>
/// SICK-04: the deterministic rules evaluated by <see cref="Services.AttendanceAlertEvaluationService"/>.
/// Each rule is purely informational and never mutates <see cref="SicknessRecord"/>, employment or
/// disciplinary state — see AttendanceAlert.
/// </summary>
internal enum AttendanceAlertRule
{
    /// <summary>Four or more (configurable) separate absence spells within a rolling window.</summary>
    FrequentAbsences,

    /// <summary>A single weekday (e.g. every Monday) recurring as the absence start day within a rolling window.</summary>
    WeekdayPattern,

    /// <summary>A single absence spell whose duration meets or exceeds the configured long-absence threshold.</summary>
    LongAbsence,

    /// <summary>A return-to-work review that is overdue, or missing entirely for a record that required one.</summary>
    MissingReturnToWorkReview,
}
