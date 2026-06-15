using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCreator(TasksDbContext dbContext, IClock clock, IAuditEventPublisher auditPublisher) : ITaskCreator
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
        Guid? sourceEntityId,
        CancellationToken cancellationToken)
    {
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            title.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            priority, source, dueDate, assignedEmployeeId, assignedUserId,
            clock.UtcNowOffset(), sourceEntityId);

        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new TaskCreatedAuditEvent(
            task.CompanyId,
            task.Id,
            task.CreatedBy,
            task.Title,
            task.Priority.ToString(),
            task.Source.ToString(),
            task.AssignedEmployeeId,
            task.AssignedUserId,
            task.CreatedAt), cancellationToken);

        return task.Id;
    }
}
