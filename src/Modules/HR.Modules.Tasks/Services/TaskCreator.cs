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
            // NOT-03: TaskAssigned is one of the six template-backed notification types (see
            // NotificationTemplateCatalogue). The rendered in-app title/body reproduce exactly what
            // the previous inline "$New task assigned: {task.Title}$" / task.Description strings
            // produced.
            var tokens = new Dictionary<string, string> { ["TaskTitle"] = task.Title };
            if (!string.IsNullOrWhiteSpace(task.Description))
                tokens["TaskDescription"] = task.Description;

            var writeResult = await notificationWriter.WriteTemplatedAsync(
                Guid.NewGuid(), companyId, assignedEmployeeId.Value,
                NotificationType.TaskAssigned,
                tokens,
                task.Id,
                ToNotificationPriority(priority),
                clock.UtcNowOffset(),
                cancellationToken);

            // TaskTitle is always present (see above), so this should never actually fail — but
            // surface it loudly rather than silently swallowing a template regression.
            if (writeResult.IsFailure)
                throw new InvalidOperationException($"Failed to write TaskAssigned notification: {writeResult.Error.Message}");
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
