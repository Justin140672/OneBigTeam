using HR.Web.Components.Pages.Dashboards;

namespace HR.Web.Tests;

/// <summary>
/// DSH-03 — <see cref="WidgetPanelState.Summarise"/> is the pure aggregation that lets a dashboard
/// widget panel tell a genuine "all clear" (every source loaded and empty) apart from a partial or
/// total failure. One test per acceptance scenario in the ticket.
///
/// Observed implementation behaviour (pinned here deliberately):
///   ShowAllClear == AllRequiredLoaded &amp;&amp; !AnyFailed &amp;&amp; TotalActionableCount == 0
/// i.e. ShowAllClear requires the STRONGER <c>!AnyFailed</c> condition, not merely
/// <c>AllRequiredLoaded</c>. A failure of a NON-required source therefore still suppresses the
/// all-clear block (see NonRequiredSourceFailed_AllRequiredLoadedAndEmpty_StillNotAllClear).
/// </summary>
public class WidgetPanelStateTests
{
    private static WidgetSourceOutcome Ok(string name, int count, bool required = true) =>
        new(name, required, Failed: false, ActionableCount: count);

    private static WidgetSourceOutcome Fail(string name, bool required = true) =>
        new(name, required, Failed: true, ActionableCount: 0);

    [Fact]
    public void TotalSuccess_WithActionableRecords_IsNotAllClear_AndSumsCounts()
    {
        var summary = WidgetPanelState.Summarise(
        [
            Ok("Leave requests", 3),
            Ok("Probation reviews", 2),
        ]);

        Assert.False(summary.ShowAllClear);
        Assert.False(summary.AnyFailed);
        Assert.True(summary.AllRequiredLoaded);
        Assert.False(summary.HasPartialFailure);
        Assert.False(summary.TotalFailure);
        Assert.Empty(summary.FailedSources);
        Assert.Equal(5, summary.TotalActionableCount);
    }

    [Fact]
    public void EmptySuccess_EverySourceLoadedWithZeroActionable_IsAllClear()
    {
        var summary = WidgetPanelState.Summarise(
        [
            Ok("Leave requests", 0),
            Ok("Probation reviews", 0),
        ]);

        Assert.True(summary.ShowAllClear);
        Assert.False(summary.AnyFailed);
        Assert.True(summary.AllRequiredLoaded);
        Assert.False(summary.HasPartialFailure);
        Assert.False(summary.TotalFailure);
        Assert.Equal(0, summary.TotalActionableCount);
    }

    [Fact]
    public void PartialFailure_SomeLoadedWithRecords_OneRequiredFailed()
    {
        var summary = WidgetPanelState.Summarise(
        [
            Ok("Leave requests", 4),
            Ok("Probation reviews", 1),
            Fail("Document reviews"),
        ]);

        Assert.False(summary.ShowAllClear);
        Assert.True(summary.AnyFailed);
        Assert.False(summary.AllRequiredLoaded);
        Assert.True(summary.HasPartialFailure);
        Assert.False(summary.TotalFailure);
        Assert.Contains("Document reviews", summary.FailedSources);
        Assert.Equal(5, summary.TotalActionableCount);
    }

    [Fact]
    public void PartialFailure_NonFailedSourcesAllEmpty_StillNotAllClear()
    {
        var summary = WidgetPanelState.Summarise(
        [
            Ok("Leave requests", 0),
            Ok("Probation reviews", 0),
            Fail("Document reviews"),
        ]);

        Assert.False(summary.ShowAllClear);
        Assert.True(summary.AnyFailed);
        Assert.True(summary.HasPartialFailure);
        Assert.Equal(0, summary.TotalActionableCount);
    }

    [Fact]
    public void TotalFailure_EverySourceFailed()
    {
        var summary = WidgetPanelState.Summarise(
        [
            Fail("Leave requests"),
            Fail("Probation reviews"),
        ]);

        Assert.True(summary.TotalFailure);
        Assert.False(summary.ShowAllClear);
        Assert.True(summary.AnyFailed);
        Assert.False(summary.AllRequiredLoaded);
        Assert.False(summary.HasPartialFailure);
        Assert.Equal(2, summary.FailedSources.Count);
    }

    [Fact]
    public void NonRequiredSourceFailed_AllRequiredLoadedAndEmpty_StillNotAllClear()
    {
        var summary = WidgetPanelState.Summarise(
        [
            Ok("Leave requests", 0),
            Fail("Team lookups", required: false),
        ]);

        Assert.True(summary.AllRequiredLoaded);
        Assert.True(summary.AnyFailed);
        Assert.False(summary.ShowAllClear);
        Assert.True(summary.HasPartialFailure);
        Assert.Contains("Team lookups", summary.FailedSources);
    }

    [Fact]
    public void Retry_PreviouslyFailedSourceNowLoaded_FlipsToAllClearlyRecovered()
    {
        var beforeRetry = WidgetPanelState.Summarise(
        [
            Ok("Leave requests", 2),
            Fail("Document reviews"),
        ]);

        Assert.True(beforeRetry.AnyFailed);
        Assert.False(beforeRetry.ShowAllClear);
        Assert.True(beforeRetry.HasPartialFailure);

        var afterRetry = WidgetPanelState.Summarise(
        [
            Ok("Leave requests", 2),
            Ok("Document reviews", 3),
        ]);

        Assert.False(afterRetry.AnyFailed);
        Assert.Empty(afterRetry.FailedSources);
        Assert.True(afterRetry.AllRequiredLoaded);
        Assert.False(afterRetry.HasPartialFailure);
        Assert.Equal(5, afterRetry.TotalActionableCount);
        Assert.False(afterRetry.ShowAllClear);
    }

    [Fact]
    public void Retry_RecoversToEmpty_ThenAllClear()
    {
        var afterRetryEmpty = WidgetPanelState.Summarise(
        [
            Ok("Leave requests", 0),
            Ok("Document reviews", 0),
        ]);

        Assert.False(afterRetryEmpty.AnyFailed);
        Assert.True(afterRetryEmpty.ShowAllClear);
    }

    [Fact]
    public void EmptyOutcomeSet_IsNeitherAllClearNorFailure()
    {
        var summary = WidgetPanelState.Summarise([]);

        Assert.False(summary.TotalFailure);
        Assert.False(summary.AnyFailed);
        Assert.True(summary.AllRequiredLoaded);
        Assert.True(summary.ShowAllClear);
    }
}
