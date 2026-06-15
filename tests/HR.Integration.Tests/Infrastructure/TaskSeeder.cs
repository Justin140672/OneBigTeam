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

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy ?? Guid.NewGuid(),
            title, description, priority, source, dueDate,
            assignedEmployeeId, assignedUserId, DateTimeOffset.UtcNow);

        if (status == TaskItemStatus.InProgress) task.Start(DateTimeOffset.UtcNow);
        if (status == TaskItemStatus.Completed)  task.Complete(Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (status == TaskItemStatus.Cancelled)  task.Cancel(DateTimeOffset.UtcNow);

        db.TaskItems.Add(task);
        await db.SaveChangesAsync();

        return task.Id;
    }
}
