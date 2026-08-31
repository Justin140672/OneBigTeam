namespace HR.Web.Components.Pages.Dashboards;

/// <summary>
/// The observed outcome of one data source feeding a dashboard widget panel.
/// </summary>
/// <param name="SourceName">Stable, human-readable name (e.g. "Leave requests").</param>
/// <param name="Required">Whether a failure of this source degrades the whole panel (lookups are not required).</param>
/// <param name="Failed">True if loading the source threw / could not complete.</param>
/// <param name="ActionableCount">Number of actionable rows this source contributed (ignored when <paramref name="Failed"/>).</param>
public readonly record struct WidgetSourceOutcome(string SourceName, bool Required, bool Failed, int ActionableCount);

/// <summary>
/// Aggregated view of every source feeding one widget panel — the signal the UI uses to decide
/// between a genuine "all clear", a partial-failure warning, and a total-failure state.
/// </summary>
public sealed record WidgetPanelSummary(
    IReadOnlyList<string> FailedSources,
    bool AnyFailed,
    bool AllRequiredLoaded,
    int TotalActionableCount,
    bool ShowAllClear,
    bool HasPartialFailure,
    bool TotalFailure);

/// <summary>
/// Pure, DI-free state logic for a dashboard widget panel (the DSH-03 analogue of
/// AdministrationHubCategories / ManagerAttentionQueueOrdering). Distinguishes a source failure
/// from a genuine empty result so widgets stop showing a misleading "all clear" / zero.
/// </summary>
public static class WidgetPanelState
{
    public static WidgetPanelSummary Summarise(IEnumerable<WidgetSourceOutcome> outcomes)
    {
        var list = outcomes as IReadOnlyList<WidgetSourceOutcome> ?? outcomes.ToList();

        var failedSources = list.Where(o => o.Failed).Select(o => o.SourceName).ToList();
        var anyFailed = failedSources.Count > 0;
        var allRequiredLoaded = list.Where(o => o.Required).All(o => !o.Failed);
        var totalActionableCount = list.Where(o => !o.Failed).Sum(o => o.ActionableCount);
        var anySucceeded = list.Any(o => !o.Failed);
        var totalFailure = list.Count > 0 && list.All(o => o.Failed);

        var showAllClear = allRequiredLoaded && !anyFailed && totalActionableCount == 0;
        var hasPartialFailure = anyFailed && anySucceeded;

        return new WidgetPanelSummary(
            FailedSources: failedSources,
            AnyFailed: anyFailed,
            AllRequiredLoaded: allRequiredLoaded,
            TotalActionableCount: totalActionableCount,
            ShowAllClear: showAllClear,
            HasPartialFailure: hasPartialFailure,
            TotalFailure: totalFailure);
    }
}
