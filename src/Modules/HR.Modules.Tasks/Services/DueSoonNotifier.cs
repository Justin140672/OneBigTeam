using HR.Modules.Notifications;
using HR.Modules.Notifications.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HR.Modules.Tasks.Services;

internal sealed class DueSoonNotifier(IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const int DueSoonDays = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckTaskAlertsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // swallow — will retry next interval
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckTaskAlertsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext          = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var notificationWriter = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
        var clock              = scope.ServiceProvider.GetRequiredService<IClock>();

        var now    = clock.UtcNowOffset();
        var today  = DateOnly.FromDateTime(now.UtcDateTime);
        var cutoff = today.AddDays(DueSoonDays);

        var candidates = await dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.DueDate.HasValue
                     && t.AssignedEmployeeId.HasValue
                     && (t.Status == TaskItemStatus.Open || t.Status == TaskItemStatus.InProgress))
            .ToListAsync(ct);

        foreach (var task in candidates)
        {
            var isOverdue = task.DueDate!.Value < today;
            var isDueSoon = !isOverdue && task.DueDate.Value <= cutoff;

            if (isOverdue)
            {
                await MaybeCreateAsync(
                    notificationWriter, task, now,
                    NotificationType.TaskOverdue, NotificationPriority.Urgent,
                    $"Overdue: {task.Title}",
                    $"This task was due on {task.DueDate.Value:d MMM yyyy} and has not been completed.",
                    ct);
            }
            else if (isDueSoon)
            {
                var dueToday = task.DueDate.Value == today;
                await MaybeCreateAsync(
                    notificationWriter, task, now,
                    NotificationType.TaskDueSoon, NotificationPriority.High,
                    $"Due {(dueToday ? "today" : "soon")}: {task.Title}",
                    dueToday
                        ? "This task is due today."
                        : $"This task is due on {task.DueDate.Value:d MMM yyyy}.",
                    ct);
            }
        }
    }

    private static async Task MaybeCreateAsync(
        INotificationWriter notificationWriter,
        TaskItem task,
        DateTimeOffset now,
        NotificationType type,
        NotificationPriority priority,
        string title,
        string body,
        CancellationToken ct)
    {
        var exists = await notificationWriter.ExistsAsync(
            task.AssignedEmployeeId!.Value, task.Id, type, ct);

        if (exists) return;

        await notificationWriter.WriteAsync(
            Guid.NewGuid(), task.CompanyId, task.AssignedEmployeeId!.Value,
            title, body, task.Id, type, priority, now, ct);
    }
}
