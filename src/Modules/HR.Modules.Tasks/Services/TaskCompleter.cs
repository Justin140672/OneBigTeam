using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
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

        if (task is null)
            return;

        var previousStatus = task.Status.ToString();
        var now = clock.UtcNowOffset();
        task.Complete(completedBy, now);
        await dbContext.SaveChangesAsync(cancellationToken);

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
