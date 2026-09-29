namespace HR.Web.Components.Pages.Dashboards;

public readonly record struct WidgetSourceOutcome(string SourceName, bool Required, bool Failed, int ActionableCount);

public sealed record WidgetPanelSummary(
    IReadOnlyList<string> FailedSources,
    bool AnyFailed,
    bool AllRequiredLoaded,
    int TotalActionableCount,
    bool ShowAllClear,
    bool HasPartialFailure,
    bool TotalFailure);

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
