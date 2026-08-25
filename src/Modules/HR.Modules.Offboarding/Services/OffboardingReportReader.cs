using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Services;

internal sealed class OffboardingReportReader(OffboardingDbContext dbContext) : IOffboardingReportReader
{
    // Literal title created by StartOffboarding/Handler.cs for every plan — the closest existing
    // signal to "documents returned" (see OffboardingReportItem.DocumentsReturned doc comment).
    private const string DocumentReviewTaskTitle = "Review outstanding documents for employee exit";

    // Row cap (OBT-720 perf pass) — see HR.Modules.Sickness.Services.SicknessReportReader.MaxRows
    // for rationale. Applied to the raw plan rows (each employee typically has 1-3 plans across
    // their tenure), well above the report's final one-row-per-employee output size.
    private const int MaxPlanRows = 50_000;

    public async Task<IReadOnlyList<OffboardingReportItem>> GetOffboardingReportAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var plans = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.Id)
            .Take(MaxPlanRows)
            .ToListAsync(cancellationToken);

        var latestPlans = plans
            .GroupBy(p => p.EmployeeId)
            .Select(g => g.OrderByDescending(p => p.CreatedAt).First())
            .ToList();

        if (latestPlans.Count == 0)
            return [];

        var planIds = latestPlans.Select(p => p.Id).ToList();

        var tasks = await dbContext.OffboardingTasks
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && planIds.Contains(t.OffboardingPlanId))
            .ToListAsync(cancellationToken);

        var tasksByPlan = tasks.ToLookup(t => t.OffboardingPlanId);

        var results = new List<OffboardingReportItem>();
        foreach (var plan in latestPlans)
        {
            var planTasks = tasksByPlan[plan.Id].ToList();

            // OFF-07: uses the same OffboardingProgressCalculator as GetOffboardingOverviewHandler
            // (and, by extension, the Blazor UI, which now displays the overview's server-computed
            // numbers rather than recomputing them) — this used to only count Status == Completed as
            // "done", silently excluding Skipped tasks and disagreeing with the UI's own
            // Completed-or-Skipped definition of progress. CompletedTasks below is therefore
            // "resolved" (Completed + Skipped), matching the UI exactly.
            var progress = OffboardingProgressCalculator.Calculate(planTasks);
            var resolvedTitles = planTasks
                .Where(t => t.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped)
                .Select(t => t.Title)
                .ToList();
            var outstanding = planTasks
                .Where(t => t.Status != OffboardingTaskStatus.Completed && t.Status != OffboardingTaskStatus.Skipped)
                .Select(t => t.Title)
                .ToList();

            var documentReviewTask = planTasks.FirstOrDefault(t => t.Title == DocumentReviewTaskTitle);
            var documentsReturned = documentReviewTask is null
                || documentReviewTask.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped;

            results.Add(new OffboardingReportItem(
                plan.EmployeeId,
                plan.LastWorkingDay,
                plan.Status.ToString(),
                progress.TotalTasks,
                progress.ResolvedTasks,
                outstanding,
                resolvedTitles,
                documentsReturned));
        }

        return results;
    }
}
