using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

internal static class TaskSeeder
{
    public static async Task<Guid> SeedAsync(
        ApiWebApplicationFactory factory,
        Guid companyId,
        string title = "Test Task",
        string? description = null,
        TaskPriority priority = TaskPriority.Medium,
        TaskSource source = TaskSource.Manual,
        DateOnly? dueDate = null,
        Guid? assignedEmployeeId = null,
        Guid? assignedUserId = null,
        Guid? createdBy = null,
        TaskItemStatus status = TaskItemStatus.Open)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var now  = DateTimeOffset.UtcNow;
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy ?? Guid.NewGuid(),
            title, description, priority, source, dueDate,
            assignedEmployeeId, assignedUserId, now);

        if (status == TaskItemStatus.InProgress) task.Start(now);
        if (status == TaskItemStatus.Completed)  task.Complete(Guid.NewGuid(), now);
        if (status == TaskItemStatus.Cancelled)  task.Cancel(now);

        db.TaskItems.Add(task);

        if (assignedEmployeeId.HasValue)
        {
            db.Notifications.Add(Notification.Create(
                Guid.NewGuid(), companyId, assignedEmployeeId.Value,
                $"New task assigned: {task.Title}",
                task.Description,
                task.Id,
                now,
                NotificationType.TaskAssigned));
        }

        await db.SaveChangesAsync();

        return task.Id;
    }
}
