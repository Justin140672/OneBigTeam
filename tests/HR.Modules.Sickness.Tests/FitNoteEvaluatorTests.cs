using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Tests;

public class FitNoteEvaluatorTests
{
    // EvaluateOnCreate
    // Note: FitNoteRequiredAfterDays is mandatory now (no opt-out — see
    // CompanySettings.FitNoteRequiredAfterDays), so the "setting is null" cases these used to
    // cover can no longer occur and have been removed.

    [Fact]
    public void EvaluateOnCreate_Returns_Pending_When_TotalDays_Is_Null_And_Setting_Is_Set()
    {
        // Open record — no end date yet, fit note setting enabled → Pending by default
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, totalDays: null);
        Assert.Equal(SicknessEvidenceStatus.Pending, result);
    }

    [Fact]
    public void EvaluateOnCreate_Returns_NotRequired_When_TotalDays_Below_Threshold()
    {
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, totalDays: 3m);
        Assert.Equal(SicknessEvidenceStatus.NotRequired, result);
    }

    [Fact]
    public void EvaluateOnCreate_Returns_Pending_When_TotalDays_Equals_Threshold()
    {
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, totalDays: 7m);
        Assert.Equal(SicknessEvidenceStatus.Pending, result);
    }

    [Fact]
    public void EvaluateOnCreate_Returns_Pending_When_TotalDays_Above_Threshold()
    {
        var result = FitNoteEvaluator.EvaluateOnCreate(fitNoteRequiredAfterDays: 7, totalDays: 10m);
        Assert.Equal(SicknessEvidenceStatus.Pending, result);
    }

    // EvaluateOnClose

    [Fact]
    public void EvaluateOnClose_Does_Not_Override_Received_Status()
    {
        var result = FitNoteEvaluator.EvaluateOnClose(SicknessEvidenceStatus.Received, fitNoteRequiredAfterDays: 7, totalDays: 10m);
        Assert.Equal(SicknessEvidenceStatus.Received, result);
    }

    [Fact]
    public void EvaluateOnClose_Does_Not_Override_Waived_Status()
    {
        var result = FitNoteEvaluator.EvaluateOnClose(SicknessEvidenceStatus.Waived, fitNoteRequiredAfterDays: 7, totalDays: 10m);
        Assert.Equal(SicknessEvidenceStatus.Waived, result);
    }

    [Fact]
    public void EvaluateOnClose_Re_Evaluates_NotRequired_Status_To_Pending_When_Above_Threshold()
    {
        var result = FitNoteEvaluator.EvaluateOnClose(SicknessEvidenceStatus.NotRequired, fitNoteRequiredAfterDays: 7, totalDays: 10m);
        Assert.Equal(SicknessEvidenceStatus.Pending, result);
    }

    [Fact]
    public void EvaluateOnClose_Re_Evaluates_Pending_Status_To_NotRequired_When_Below_Threshold()
    {
        var result = FitNoteEvaluator.EvaluateOnClose(SicknessEvidenceStatus.Pending, fitNoteRequiredAfterDays: 7, totalDays: 3m);
        Assert.Equal(SicknessEvidenceStatus.NotRequired, result);
    }

}
