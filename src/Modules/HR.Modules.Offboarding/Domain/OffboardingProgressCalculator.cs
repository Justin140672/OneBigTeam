namespace HR.Modules.Offboarding.Domain;

// OFF-07: single source of truth for "how complete is this plan" — previously computed separately
// (and inconsistently) in GetOffboardingOverviewHandler/EmployeeOffboardingTab.razor (Completed +
// Skipped counted as done) and in OffboardingReportReader (only Completed counted as done, Skipped
// tasks silently excluded from both the numerator and any "outstanding" list). Every reader
// (GetOffboardingOverview's response, the Reporting module's cross-module IOffboardingReportReader,
// and the Blazor UI, which now just displays the server-computed numbers instead of recomputing
// them) calls this so the reported progress can never drift between them again.
internal readonly record struct OffboardingProgressSummary(
    int TotalTasks,
    int CompletedTasks,
    int SkippedTasks,
    int ResolvedTasks,
    int ProgressPercent,
    bool CanComplete);

internal static class OffboardingProgressCalculator
{
    public static OffboardingProgressSummary Calculate(IReadOnlyCollection<OffboardingTask> tasks)
    {
        var total = tasks.Count;

        if (total == 0)
            return new OffboardingProgressSummary(0, 0, 0, 0, 0, false);

        var completed = tasks.Count(t => t.Status == OffboardingTaskStatus.Completed);
        var skipped = tasks.Count(t => t.Status == OffboardingTaskStatus.Skipped);
        var resolved = completed + skipped;
        var percent = (int)Math.Round(resolved * 100.0 / total);

        return new OffboardingProgressSummary(
            total, completed, skipped, resolved, percent, OffboardingPlan.CanComplete(tasks));
    }
}
