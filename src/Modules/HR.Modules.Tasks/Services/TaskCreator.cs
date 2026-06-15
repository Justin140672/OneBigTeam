using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCreator(TasksDbContext dbContext, IClock clock) : ITaskCreator
{
    public async Task<Guid> CreateAsync(
        Guid companyId,
        Guid createdBy,
        string title,
        string? description,
        TaskPriority priority,
        TaskSource source,
        DateOnly? dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId,
        CancellationToken cancellationToken)
    {
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            title.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            priority, source, dueDate, assignedEmployeeId, assignedUserId,
            clock.UtcNowOffset());

        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);

        return task.Id;
    }
}
