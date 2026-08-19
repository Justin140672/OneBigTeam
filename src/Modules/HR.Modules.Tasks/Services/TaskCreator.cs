using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCreator(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher) : ITaskCreator
{
    public async Task<Guid> CreateAsync(
        Guid companyId,
        Guid createdBy,
        string title,
        string? description,
        TaskPriority priority,
        TaskSource source,
        TaskActionType actionType,
        DateOnly? dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId,
        Guid? sourceEntityId,
        CancellationToken cancellationToken,
        bool notifyAssignee = true)
    {
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            title.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            priority, source, actionType, dueDate, assignedEmployeeId, assignedUserId,
            clock.UtcNowOffset(), sourceEntityId);

        dbContext.TaskItems.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (assignedEmployeeId.HasValue && notifyAssignee)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(), companyId, assignedEmployeeId.Value,
                $"New task assigned: {task.Title}",
                task.Description,
                task.Id,
                NotificationType.TaskAssigned,
                ToNotificationPriority(priority),
                clock.UtcNowOffset(),
                cancellationToken);
        }

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

    private static NotificationPriority ToNotificationPriority(TaskPriority priority) => priority switch
    {
        TaskPriority.Critical => NotificationPriority.Urgent,
        TaskPriority.High     => NotificationPriority.High,
        TaskPriority.Medium   => NotificationPriority.Normal,
        _                     => NotificationPriority.Low,
    };
}
