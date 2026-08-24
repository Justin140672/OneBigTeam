using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Tests;

public class FitNoteEvaluatorTests
{
    // EvaluateOnCreate
    // Note: FitNoteRequiredAfterDays is mandatory now (no opt-out — see
    // CompanySettings.FitNoteRequiredAfterDays), so the "setting is null" cases these used to
    // cover can no longer occur and have been removed.
    //
    // SICK-01: thresholds are calendar-day based (inclusive — start date is day 1), independently
    // of the working-day TotalDays total.

    private static readonly DateOnly StartDate = new(2026, 6, 1);

    [Fact]
    public void EvaluateOnCreate_Returns_Pending_When_Ongoing_No_EndDate()
    {
        // Open record — no end date yet, duration unknown → Pending by default
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, startDate: StartDate, endDate: null);
        Assert.Equal(SicknessEvidenceStatus.Pending, result);
    }

    [Fact]
    public void EvaluateOnCreate_Returns_NotRequired_When_CalendarDays_Below_Threshold()
    {
        // 2026-06-01 to 2026-06-03 = 3 calendar days elapsed, threshold 7 → NotRequired
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, startDate: StartDate, endDate: StartDate.AddDays(2));
        Assert.Equal(SicknessEvidenceStatus.NotRequired, result);
    }

    [Fact]
    public void EvaluateOnCreate_Returns_Pending_When_CalendarDays_Equals_Threshold()
    {
        // Inclusive semantics: start date + 6 = 7 calendar days elapsed
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, startDate: StartDate, endDate: StartDate.AddDays(6));
        Assert.Equal(SicknessEvidenceStatus.Pending, result);
    }

    [Fact]
    public void EvaluateOnCreate_Returns_NotRequired_OneDayBeforeThreshold()
    {
        // start date + 5 = 6 calendar days elapsed, threshold 7 → still NotRequired (boundary case)
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, startDate: StartDate, endDate: StartDate.AddDays(5));
        Assert.Equal(SicknessEvidenceStatus.NotRequired, result);
    }

    [Fact]
    public void EvaluateOnCreate_Returns_Pending_When_CalendarDays_Above_Threshold()
    {
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, startDate: StartDate, endDate: StartDate.AddDays(20));
        Assert.Equal(SicknessEvidenceStatus.Pending, result);
    }

    [Fact]
    public void EvaluateOnCreate_Counts_Weekends_Toward_Threshold()
    {
        // 2026-06-01 (Mon) to 2026-06-07 (Sun), spanning a full weekend = 7 calendar days elapsed
        var start = new DateOnly(2026, 6, 1);
        var end = new DateOnly(2026, 6, 7);
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, startDate: start, endDate: end);
        Assert.Equal(SicknessEvidenceStatus.Pending, result);
    }

    // EvaluateOnClose

    [Fact]
    public void EvaluateOnClose_Does_Not_Override_Received_Status()
    {
        var result = FitNoteEvaluator.EvaluateOnClose(
            SicknessEvidenceStatus.Received, fitNoteRequiredAfterDays: 7, startDate: StartDate, endDate: StartDate.AddDays(20));
        Assert.Equal(SicknessEvidenceStatus.Received, result);
    }

    [Fact]
    public void EvaluateOnClose_Does_Not_Override_Waived_Status()
    {
        var result = FitNoteEvaluator.EvaluateOnClose(
            SicknessEvidenceStatus.Waived, fitNoteRequiredAfterDays: 7, startDate: StartDate, endDate: StartDate.AddDays(20));
        Assert.Equal(SicknessEvidenceStatus.Waived, result);
    }

    [Fact]
    public void EvaluateOnClose_Re_Evaluates_NotRequired_Status_To_Pending_When_Above_Threshold()
    {
        var result = FitNoteEvaluator.EvaluateOnClose(
            SicknessEvidenceStatus.NotRequired, fitNoteRequiredAfterDays: 7, startDate: StartDate, endDate: StartDate.AddDays(20));
        Assert.Equal(SicknessEvidenceStatus.Pending, result);
    }

    [Fact]
    public void EvaluateOnClose_Re_Evaluates_Pending_Status_To_NotRequired_When_Below_Threshold()
    {
        var result = FitNoteEvaluator.EvaluateOnClose(
            SicknessEvidenceStatus.Pending, fitNoteRequiredAfterDays: 7, startDate: StartDate, endDate: StartDate.AddDays(2));
        Assert.Equal(SicknessEvidenceStatus.NotRequired, result);
    }

    // CalculateCalendarDaysElapsed / IsThresholdReached

    [Fact]
    public void CalculateCalendarDaysElapsed_StartDate_Counts_As_Day_One()
    {
        Assert.Equal(1, FitNoteEvaluator.CalculateCalendarDaysElapsed(StartDate, StartDate));
    }

    [Fact]
    public void CalculateCalendarDaysElapsed_Is_Inclusive_Of_Both_Ends()
    {
        Assert.Equal(7, FitNoteEvaluator.CalculateCalendarDaysElapsed(StartDate, StartDate.AddDays(6)));
    }

    [Fact]
    public void IsThresholdReached_False_Below_Threshold()
    {
        Assert.False(FitNoteEvaluator.IsThresholdReached(StartDate, StartDate.AddDays(5), 7));
    }

    [Fact]
    public void IsThresholdReached_True_At_Boundary()
    {
        Assert.True(FitNoteEvaluator.IsThresholdReached(StartDate, StartDate.AddDays(6), 7));
    }
}
