using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Jobs;

internal sealed class OnboardingReminderJob(
    OnboardingDbContext dbContext,
    IManagerReader managerReader,
    INotificationWriter notificationWriter,
    ICompanyTimeZoneReader timeZoneReader,
    IClock clock)
{
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        // DueDate due-ness depends on each company's own configured time zone, so pending/
        // in-progress tasks with any past-or-present due date are fetched broadly first and then
        // filtered per company below using that company's "today".
        var candidateTasks = await dbContext.OnboardingTasks
            .AsNoTracking()
            .Where(t => t.DueDate != null
                && (t.Status == OnboardingTaskStatus.Pending || t.Status == OnboardingTaskStatus.InProgress))
            .ToListAsync();

        if (candidateTasks.Count == 0)
            return;

        var planIds = candidateTasks.Select(t => t.OnboardingPlanId).Distinct().ToList();

        var plans = await dbContext.OnboardingPlans
            .AsNoTracking()
            .Where(p => planIds.Contains(p.Id))
            .ToListAsync();

        var plansById = plans.ToDictionary(p => p.Id);

        var todayByCompany = new Dictionary<Guid, DateOnly>();
        var overdueTasks = new List<OnboardingTask>();

        foreach (var task in candidateTasks)
        {
            if (!plansById.TryGetValue(task.OnboardingPlanId, out var plan))
                continue;

            if (!todayByCompany.TryGetValue(plan.CompanyId, out var today))
            {
                var timeZoneId = await timeZoneReader.GetTimeZoneAsync(plan.CompanyId, CancellationToken.None);
                today = clock.TodayIn(timeZoneId);
                todayByCompany[plan.CompanyId] = today;
            }

            if (task.DueDate < today)
                overdueTasks.Add(task);
        }

        foreach (var task in overdueTasks)
        {
            if (!plansById.TryGetValue(task.OnboardingPlanId, out var plan))
                continue;

            switch (task.AssignTo)
            {
                case OnboardingTemplateTaskAssignTo.Manager:
                    await NotifyManagerAsync(task, plan, now);
                    break;

                case OnboardingTemplateTaskAssignTo.NewHire:
                    await NotifyEmployeeAsync(task, plan, now);
                    break;

                case OnboardingTemplateTaskAssignTo.Unassigned:
                default:
                    break;
            }
        }
    }

    private async Task NotifyManagerAsync(OnboardingTask task, OnboardingPlan plan, DateTimeOffset now)
    {
        var managerId = await managerReader.GetManagerIdAsync(plan.CompanyId, plan.EmployeeId, CancellationToken.None);
        if (managerId is null)
            return;

        var alreadySent = await notificationWriter.ExistsAsync(
            managerId.Value, task.Id, NotificationType.OnboardingTaskOverdue);

        if (alreadySent)
            return;

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            plan.CompanyId,
            managerId.Value,
            "Overdue onboarding task",
            $"\"{task.Title}\" was due {task.DueDate:d MMM yyyy} and is still outstanding.",
            task.Id,
            NotificationType.OnboardingTaskOverdue,
            NotificationPriority.High,
            now);
    }

    private async Task NotifyEmployeeAsync(OnboardingTask task, OnboardingPlan plan, DateTimeOffset now)
    {
        var alreadySent = await notificationWriter.ExistsAsync(
            plan.EmployeeId, task.Id, NotificationType.OnboardingTaskOverdue);

        if (alreadySent)
            return;

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            plan.CompanyId,
            plan.EmployeeId,
            "Overdue onboarding task",
            $"\"{task.Title}\" was due {task.DueDate:d MMM yyyy} and is still outstanding.",
            task.Id,
            NotificationType.OnboardingTaskOverdue,
            NotificationPriority.High,
            now);
    }
}
