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
                await CheckDueSoonAsync(stoppingToken);
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

    private async Task CheckDueSoonAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var clock    = scope.ServiceProvider.GetRequiredService<IClock>();

        var now      = clock.UtcNowOffset();
        var today    = DateOnly.FromDateTime(now.UtcDateTime);
        var cutoff   = today.AddDays(DueSoonDays);

        var candidates = await dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.DueDate.HasValue
                     && t.DueDate.Value <= cutoff
                     && t.AssignedEmployeeId.HasValue
                     && (t.Status == TaskItemStatus.Open || t.Status == TaskItemStatus.InProgress))
            .ToListAsync(ct);

        foreach (var task in candidates)
        {
            var alreadyNotified = await dbContext.Notifications
                .AnyAsync(n => n.SourceEntityId == task.Id
                            && n.EmployeeId     == task.AssignedEmployeeId!.Value
                            && n.Type           == NotificationType.TaskDueSoon, ct);

            if (alreadyNotified) continue;

            var dueToday = task.DueDate!.Value == today;
            var notification = Notification.Create(
                Guid.NewGuid(),
                task.CompanyId,
                task.AssignedEmployeeId!.Value,
                $"Due {(dueToday ? "today" : "soon")}: {task.Title}",
                dueToday
                    ? "This task is due today."
                    : $"This task is due on {task.DueDate.Value:d MMM yyyy}.",
                task.Id,
                now,
                NotificationType.TaskDueSoon);

            dbContext.Notifications.Add(notification);
        }

        if (dbContext.ChangeTracker.HasChanges())
            await dbContext.SaveChangesAsync(ct);
    }
}
