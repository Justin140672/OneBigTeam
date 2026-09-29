namespace HR.Modules.Offboarding.Domain;

internal readonly record struct OffboardingProgressSummary(
    int TotalTasks,
    int CompletedTasks,
    int SkippedTasks,
    int ResolvedTasks,
    int ProgressPercent,
    bool CanComplete,
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
