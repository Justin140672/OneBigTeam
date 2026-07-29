using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Services;

internal sealed class OnboardingReportReader(OnboardingDbContext dbContext) : IOnboardingReportReader
{
    public async Task<IReadOnlyList<OnboardingReportItem>> GetOnboardingReportAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        CancellationToken cancellationToken)
    {
        var plansQuery = dbContext.OnboardingPlans
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId);

        if (employeeIds is not null)
            plansQuery = plansQuery.Where(p => employeeIds.Contains(p.EmployeeId));

        var plans = await plansQuery.ToListAsync(cancellationToken);

        // One row per employee — most-recently-created plan only, matching OnboardingStatusReader.
        var latestPlans = plans
            .GroupBy(p => p.EmployeeId)
            .Select(g => g.OrderByDescending(p => p.CreatedAt).First())
            .ToList();

        if (latestPlans.Count == 0)
            return [];

        var planIds = latestPlans.Select(p => p.Id).ToList();

        var tasks = await dbContext.OnboardingTasks
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && planIds.Contains(t.OnboardingPlanId))
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tasksByPlan = tasks.ToLookup(t => t.OnboardingPlanId);

        var results = new List<OnboardingReportItem>();
        foreach (var plan in latestPlans)
        {
            var planTasks = tasksByPlan[plan.Id].ToList();
            var completedTasks = planTasks.Count(t => t.Status == OnboardingTaskStatus.Completed);
            var outstanding = planTasks
                .Where(t => t.Status != OnboardingTaskStatus.Completed && t.Status != OnboardingTaskStatus.Skipped)
                .Select(t => new OnboardingReportTaskItem(
                    t.Title,
                    t.DueDate,
                    t.AssignTo.ToString(),
                    t.DueDate is not null && t.DueDate < today))
                .ToList();

            results.Add(new OnboardingReportItem(
                plan.EmployeeId,
                plan.Id,
                plan.Status.ToString(),
                plan.StartDate,
                planTasks.Count,
                completedTasks,
                outstanding));
        }

        return results;
    }
}
