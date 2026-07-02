namespace HR.Modules.Sickness.Domain;

internal static class FitNoteEvaluator
{
    /// <summary>
    /// Determines the evidence status for a new sickness record at creation time.
    /// </summary>
    internal static SicknessEvidenceStatus EvaluateOnCreate(int? fitNoteRequiredAfterDays, decimal? totalDays)
    {
        if (fitNoteRequiredAfterDays is null)
            return SicknessEvidenceStatus.NotRequired;

        // No end date yet — we cannot determine duration, so default to Pending
        if (totalDays is null)
            return SicknessEvidenceStatus.Pending;

        return totalDays >= fitNoteRequiredAfterDays
            ? SicknessEvidenceStatus.Pending
            : SicknessEvidenceStatus.NotRequired;
    }

    /// <summary>
    /// Re-evaluates the evidence status when a sickness record is closed.
    /// Does not override Received or Waived — those have been manually set.
    /// </summary>
    internal static SicknessEvidenceStatus EvaluateOnClose(
        SicknessEvidenceStatus currentStatus,
        int? fitNoteRequiredAfterDays,
        decimal totalDays)
    {
        if (currentStatus == SicknessEvidenceStatus.Received ||
            currentStatus == SicknessEvidenceStatus.Waived)
            return currentStatus;

        return EvaluateOnCreate(fitNoteRequiredAfterDays, totalDays);
    }
}
