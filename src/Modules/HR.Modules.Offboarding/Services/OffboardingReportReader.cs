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
            var completed = planTasks.Where(t => t.Status == OffboardingTaskStatus.Completed).ToList();
            var outstanding = planTasks.Where(t => t.Status != OffboardingTaskStatus.Completed && t.Status != OffboardingTaskStatus.Skipped).ToList();

            var documentReviewTask = planTasks.FirstOrDefault(t => t.Title == DocumentReviewTaskTitle);
            var documentsReturned = documentReviewTask is null || documentReviewTask.Status == OffboardingTaskStatus.Completed;

            results.Add(new OffboardingReportItem(
                plan.EmployeeId,
                plan.LastWorkingDay,
                plan.Status.ToString(),
                planTasks.Count,
                completed.Count,
                outstanding.Select(t => t.Title).ToList(),
                completed.Select(t => t.Title).ToList(),
                documentsReturned));
        }

        return results;
    }
}
