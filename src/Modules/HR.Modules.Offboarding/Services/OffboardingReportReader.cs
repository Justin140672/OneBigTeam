using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Services;

internal sealed class OffboardingReportReader(OffboardingDbContext dbContext) : IOffboardingReportReader
{
    private const string DocumentReviewTaskTitle = "Review outstanding documents for employee exit";

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

            var progress = OffboardingProgressCalculator.Calculate(planTasks);
            var resolvedTitles = planTasks
                .Where(t => t.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped
                    or OffboardingTaskStatus.Waived)
                .Select(t => t.Title)
                .ToList();
            var outstandingTasks = planTasks
                .Where(t => t.Status is not (OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped
                    or OffboardingTaskStatus.Waived or OffboardingTaskStatus.Cancelled))
                .ToList();
            var outstanding = outstandingTasks.Select(t => t.Title).ToList();
            var outstandingIds = outstandingTasks.Select(t => t.Id).ToList();

            var documentReviewTask = planTasks.FirstOrDefault(t => t.Title == DocumentReviewTaskTitle);
            var documentsReturned = documentReviewTask is null
                || documentReviewTask.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped
                    or OffboardingTaskStatus.Waived;

            results.Add(new OffboardingReportItem(
                plan.EmployeeId,
                plan.LastWorkingDay,
                plan.Status.ToString(),
                progress.TotalTasks,
                progress.ResolvedTasks,
                outstanding,
                resolvedTitles,
                documentsReturned,
                outstandingIds));
        }

        return results;
    }
}
