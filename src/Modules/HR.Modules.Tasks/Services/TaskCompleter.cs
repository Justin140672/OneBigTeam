using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCompleter(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    TaskCompletionDispatcher dispatcher) : ITaskCompleter
{
    public async Task CompleteBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid completedBy,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.TaskItems
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId == sourceEntityId
                     && t.Source == source
                     && t.ActionType == actionType
                     && t.Status != TaskItemStatus.Completed
                     && t.Status != TaskItemStatus.Cancelled)
            .FirstOrDefaultAsync(cancellationToken);

        await CompleteAsync(task, completedBy, cancellationToken);
    }

    public async Task CompleteBySourceEntityForEmployeeAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid assignedEmployeeId,
        Guid completedBy,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.TaskItems
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId == sourceEntityId
                     && t.Source == source
                     && t.ActionType == actionType
                     && t.AssignedEmployeeId == assignedEmployeeId
                     && t.Status != TaskItemStatus.Completed
                     && t.Status != TaskItemStatus.Cancelled)
            .FirstOrDefaultAsync(cancellationToken);

        await CompleteAsync(task, completedBy, cancellationToken);
    }

    private async Task CompleteAsync(TaskItem? task, Guid completedBy, CancellationToken cancellationToken)
    {
        if (task is null)
            return;

        var previousStatus = task.Status.ToString();
        var now = clock.UtcNowOffset();
        task.Complete(completedBy, now);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Ticket: completing a task from the notifications menu left the original
        // assignment/due/overdue notification sitting in the list forever, since nothing
        // previously removed it once the underlying task was actioned. Remove all "open task"
        // notification types tied to this task now that it's done — the fresh "Task completed"
        // notification written just below is the only one that should remain for this task.
        foreach (var openTaskNotificationType in new[]
                 {
                     NotificationType.TaskAssigned,
                     NotificationType.TaskDueSoon,
                     NotificationType.TaskOverdue,
                 })
        {
            await notificationWriter.RemoveBySourceEntityAsync(
                task.CompanyId, task.Id, openTaskNotificationType, cancellationToken);
        }

        if (task.AssignedEmployeeId.HasValue)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(), task.CompanyId, task.AssignedEmployeeId.Value,
                $"Task completed: {task.Title}",
                null,
                task.Id,
                NotificationType.TaskCompleted,
                NotificationPriority.Normal,
                now,
                cancellationToken);
        }

        await auditPublisher.PublishAsync(new TaskCompletedAuditEvent(
            task.CompanyId,
            task.Id,
            completedBy,
            previousStatus,
            task.AssignedEmployeeId,
            now), cancellationToken);

        await dispatcher.DispatchAsync(new TaskCompletionContext(
            task.CompanyId,
            task.Id,
            task.Title,
            task.Description,
            task.Source,
            task.ActionType,
            task.AssignedEmployeeId,
            completedBy,
            now,
            task.SourceEntityId), cancellationToken);
    }
}
