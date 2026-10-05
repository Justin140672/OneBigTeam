using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCanceller(TasksDbContext dbContext, INotificationWriter notificationWriter, IClock clock) : ITaskCanceller
{
    public async Task CancelBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.TaskItems
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId == sourceEntityId
                     && t.Source == source
                     && t.ActionType == actionType
                     && t.Status != TaskItemStatus.Completed)
            .OrderBy(t => t.Status == TaskItemStatus.Cancelled)
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null)
            return;

        await CancelAndCleanAsync(companyId, [task], cancellationToken);
    }

    public async Task<int> CancelAllBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        var tasks = await dbContext.TaskItems
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId == sourceEntityId
                     && t.Source == source
                     && t.ActionType == actionType
                     && t.Status != TaskItemStatus.Completed)
            .ToListAsync(cancellationToken);

        return await CancelAndCleanAsync(companyId, tasks, cancellationToken);
    }

    public async Task<int> CancelManyBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        if (sourceEntityIds.Count == 0)
            return 0;

        var tasks = await dbContext.TaskItems
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId != null
                     && sourceEntityIds.Contains(t.SourceEntityId.Value)
                     && t.Source == source
                     && t.ActionType == actionType
                     && t.Status != TaskItemStatus.Completed)
            .ToListAsync(cancellationToken);

        return await CancelAndCleanAsync(companyId, tasks, cancellationToken);
    }

    private async Task<int> CancelAndCleanAsync(
        Guid companyId, List<TaskItem> tasks, CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
            return 0;

        var toCancel = tasks.Where(t => t.Status != TaskItemStatus.Cancelled).ToList();

        if (toCancel.Count > 0)
        {
            var now = clock.UtcNowOffset();
            foreach (var task in toCancel)
                task.Cancel(now);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await RemovePendingNotificationsAsync(companyId, tasks.Select(t => t.Id), cancellationToken);

        return toCancel.Count;
    }

    // Any notification already written for an open task must not outlive the cancellation; the
    // removal is idempotent so a retry after a partial failure still converges.
    private async Task RemovePendingNotificationsAsync(
        Guid companyId, IEnumerable<Guid> taskIds, CancellationToken cancellationToken)
    {
        foreach (var taskId in taskIds)
        {
            await notificationWriter.RemoveBySourceEntityAsync(companyId, taskId, NotificationType.TaskAssigned, cancellationToken);
            await notificationWriter.RemoveBySourceEntityAsync(companyId, taskId, NotificationType.TaskDueSoon, cancellationToken);
            await notificationWriter.RemoveBySourceEntityAsync(companyId, taskId, NotificationType.TaskOverdue, cancellationToken);
        }
    }
}
