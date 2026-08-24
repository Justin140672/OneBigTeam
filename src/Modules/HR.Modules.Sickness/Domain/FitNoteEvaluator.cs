namespace HR.Modules.Sickness.Domain;

/// <summary>
/// SICK-01: fit-note evidence thresholds are evaluated in calendar days, independently of the
/// working-day <c>TotalDays</c> total used elsewhere for absence reporting (see
/// SicknessCalculator). Weekends, non-working days and public holidays all count toward the
/// threshold — an employee off sick over a bank holiday weekend accrues those days just like any
/// other.
///
/// Inclusive threshold semantics: the start date itself counts as day 1, so a "7 day" threshold is
/// reached once 7 calendar days have elapsed — i.e. on start date + 6.
/// </summary>
internal static class FitNoteEvaluator
{
    /// <summary>
    /// Calendar days elapsed from <paramref name="startDate"/> through <paramref name="evaluationDate"/>,
    /// inclusive of both ends. <paramref name="evaluationDate"/> is "today" for an ongoing absence,
    /// or the absence's own end date once closed.
    /// </summary>
    internal static int CalculateCalendarDaysElapsed(DateOnly startDate, DateOnly evaluationDate) =>
        evaluationDate.DayNumber - startDate.DayNumber + 1;

    internal static bool IsThresholdReached(DateOnly startDate, DateOnly evaluationDate, int fitNoteRequiredAfterDays) =>
        CalculateCalendarDaysElapsed(startDate, evaluationDate) >= fitNoteRequiredAfterDays;

    /// <summary>
    /// Determines the evidence status for a sickness record at creation time.
    /// </summary>
    internal static SicknessEvidenceStatus EvaluateOnCreate(
        int fitNoteRequiredAfterDays, DateOnly startDate, DateOnly? endDate)
    {
        // Ongoing (no end date yet) — duration isn't known yet, so default to Pending. The daily
        // FitNoteRequestJob (and the immediate creation-time check for an already-overdue backdated
        // absence) re-evaluates against "today" as calendar days accrue.
        if (endDate is null)
            return SicknessEvidenceStatus.Pending;

        return IsThresholdReached(startDate, endDate.Value, fitNoteRequiredAfterDays)
            ? SicknessEvidenceStatus.Pending
            : SicknessEvidenceStatus.NotRequired;
    }

    /// <summary>
    /// Re-evaluates the evidence status when a sickness record is closed.
    /// Does not override Received or Waived — those have been manually set.
    /// </summary>
    internal static SicknessEvidenceStatus EvaluateOnClose(
        SicknessEvidenceStatus currentStatus,
        int fitNoteRequiredAfterDays,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (currentStatus == SicknessEvidenceStatus.Received ||
            currentStatus == SicknessEvidenceStatus.Waived)
            return currentStatus;

        return EvaluateOnCreate(fitNoteRequiredAfterDays, startDate, endDate);
    }
}
