namespace HR.Modules.Sickness.Domain;

internal static class FitNoteEvaluator
{
    internal static int CalculateCalendarDaysElapsed(DateOnly startDate, DateOnly evaluationDate) =>
        evaluationDate.DayNumber - startDate.DayNumber + 1;

    internal static bool IsThresholdReached(DateOnly startDate, DateOnly evaluationDate, int fitNoteRequiredAfterDays) =>
        CalculateCalendarDaysElapsed(startDate, evaluationDate) >= fitNoteRequiredAfterDays;

    internal static SicknessEvidenceStatus EvaluateOnCreate(
        int fitNoteRequiredAfterDays, DateOnly startDate, DateOnly? endDate)
    {
        if (endDate is null)
            return SicknessEvidenceStatus.Pending;

        return IsThresholdReached(startDate, endDate.Value, fitNoteRequiredAfterDays)
            ? SicknessEvidenceStatus.Pending
            : SicknessEvidenceStatus.NotRequired;
    }

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
