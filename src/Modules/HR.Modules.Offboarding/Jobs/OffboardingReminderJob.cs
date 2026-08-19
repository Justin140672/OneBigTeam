using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Jobs;

internal sealed class OffboardingReminderJob(
    OffboardingDbContext dbContext,
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
        var candidateTasks = await dbContext.OffboardingTasks
            .AsNoTracking()
            .Where(t => t.DueDate != null
                && (t.Status == OffboardingTaskStatus.Pending || t.Status == OffboardingTaskStatus.InProgress))
            .ToListAsync();

        if (candidateTasks.Count == 0)
            return;

        var planIds = candidateTasks.Select(t => t.OffboardingPlanId).Distinct().ToList();

        var plans = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => planIds.Contains(p.Id))
            .ToListAsync();

        var plansById = plans.ToDictionary(p => p.Id);

        var todayByCompany = new Dictionary<Guid, DateOnly>();
        var overdueTasks = new List<OffboardingTask>();

        foreach (var task in candidateTasks)
        {
            if (!plansById.TryGetValue(task.OffboardingPlanId, out var plan))
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
            if (!plansById.TryGetValue(task.OffboardingPlanId, out var plan))
                continue;

            switch (task.AssignTo)
            {
                case OffboardingTaskAssignTo.Manager:
                    await NotifyManagerAsync(task, plan, now);
                    break;

                case OffboardingTaskAssignTo.Employee:
                    await NotifyEmployeeAsync(task, plan, now);
                    break;

                case OffboardingTaskAssignTo.HR:
                default:
                    break;
            }
        }
    }

    private async Task NotifyManagerAsync(OffboardingTask task, OffboardingPlan plan, DateTimeOffset now)
    {
        var managerId = await managerReader.GetManagerIdAsync(plan.CompanyId, plan.EmployeeId, CancellationToken.None);
        if (managerId is null)
            return;

        var alreadySent = await notificationWriter.ExistsAsync(
            managerId.Value, task.Id, NotificationType.OffboardingTaskOverdue);

        if (alreadySent)
            return;

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            plan.CompanyId,
            managerId.Value,
            "Overdue offboarding task",
            $"\"{task.Title}\" was due {task.DueDate:d MMM yyyy} and is still outstanding.",
            task.Id,
            NotificationType.OffboardingTaskOverdue,
            NotificationPriority.High,
            now);
    }

    private async Task NotifyEmployeeAsync(OffboardingTask task, OffboardingPlan plan, DateTimeOffset now)
    {
        var alreadySent = await notificationWriter.ExistsAsync(
            plan.EmployeeId, task.Id, NotificationType.OffboardingTaskOverdue);

        if (alreadySent)
            return;

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            plan.CompanyId,
            plan.EmployeeId,
            "Overdue offboarding task",
            $"\"{task.Title}\" was due {task.DueDate:d MMM yyyy} and is still outstanding.",
            task.Id,
            NotificationType.OffboardingTaskOverdue,
            NotificationPriority.High,
            now);
    }
}
