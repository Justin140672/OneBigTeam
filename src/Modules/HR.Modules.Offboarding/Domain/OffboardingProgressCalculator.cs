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
    bool CanComplete,
    // SPEC-OFF-01: "X of Y required obligations resolved" / "X of Y total obligations resolved".
    // Cancelled obligations (parent leaving process cancelled) are excluded from both counts —
    // they are never treated as completed/waived. Resolved means Completed or Waived.
    int RequiredTotal,
    int RequiredResolved,
    int TotalResolved,
    int TotalCount);

internal static class OffboardingProgressCalculator
{
    public static OffboardingProgressSummary Calculate(IReadOnlyCollection<OffboardingTask> tasks)
    {
        var total = tasks.Count;

        if (total == 0)
            return new OffboardingProgressSummary(0, 0, 0, 0, 0, false, 0, 0, 0, 0);

        var completed = tasks.Count(t => t.Status == OffboardingTaskStatus.Completed);
        var skipped = tasks.Count(t =>
            t.Status is OffboardingTaskStatus.Skipped or OffboardingTaskStatus.Waived);
        var resolved = completed + skipped;
        var percent = (int)Math.Round(resolved * 100.0 / total);

        var countable = tasks.Where(t => t.Status != OffboardingTaskStatus.Cancelled).ToList();
        bool IsResolved(OffboardingTask t) => t.Status is OffboardingTaskStatus.Completed
            or OffboardingTaskStatus.Waived or OffboardingTaskStatus.Skipped;

        var requiredTasks = countable.Where(t => t.IsMandatory).ToList();
        var requiredTotal = requiredTasks.Count;
        var requiredResolved = requiredTasks.Count(IsResolved);
        var totalCount = countable.Count;
        var totalResolved = countable.Count(IsResolved);

        return new OffboardingProgressSummary(
            total, completed, skipped, resolved, percent, OffboardingPlan.CanComplete(tasks),
            requiredTotal, requiredResolved, totalResolved, totalCount);
    }
}
