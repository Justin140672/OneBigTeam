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
                     && t.Status != TaskItemStatus.Completed
                     && t.Status != TaskItemStatus.Cancelled)
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null)
            return;

        task.Cancel(clock.UtcNowOffset());
        await dbContext.SaveChangesAsync(cancellationToken);

        await RemovePendingNotificationsAsync(companyId, [task.Id], cancellationToken);
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
                     && t.Status != TaskItemStatus.Completed
                     && t.Status != TaskItemStatus.Cancelled)
            .ToListAsync(cancellationToken);

        if (tasks.Count == 0)
            return 0;

        var now = clock.UtcNowOffset();
        foreach (var task in tasks)
            task.Cancel(now);

        await dbContext.SaveChangesAsync(cancellationToken);

        await RemovePendingNotificationsAsync(companyId, tasks.Select(t => t.Id), cancellationToken);

        return tasks.Count;
    }

    // OFF-01: bulk cancel-by-source for a caller-owned group of source entity ids (e.g. every
    // OffboardingTask.Id belonging to one OffboardingPlan). Excludes tasks already
    // Completed/Cancelled — those terminal states are never touched — which is what makes this
    // safe to call repeatedly against the exact same id set (e.g. from an idempotent event
    // consumer, or a reconciliation job retrying after a previous partial failure): the first
    // call cancels whatever is still open, every subsequent call finds nothing left to do and
    // returns 0.
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
                     && t.Status != TaskItemStatus.Completed
                     && t.Status != TaskItemStatus.Cancelled)
            .ToListAsync(cancellationToken);

        if (tasks.Count == 0)
            return 0;

        var now = clock.UtcNowOffset();
        foreach (var task in tasks)
            task.Cancel(now);

        await dbContext.SaveChangesAsync(cancellationToken);

        await RemovePendingNotificationsAsync(companyId, tasks.Select(t => t.Id), cancellationToken);

        return tasks.Count;
    }

    // Cancelling a task must not leave a stale "due soon"/"overdue" notification sitting in
    // someone's inbox referencing work that no longer needs doing. DueSoonNotifier itself will
    // never raise a *new* one for a cancelled task (it only considers Open/InProgress tasks), but
    // any notification already written before cancellation needs explicit cleanup here.
    private async Task RemovePendingNotificationsAsync(
        Guid companyId, IEnumerable<Guid> taskIds, CancellationToken cancellationToken)
    {
        foreach (var taskId in taskIds)
        {
            await notificationWriter.RemoveBySourceEntityAsync(companyId, taskId, NotificationType.TaskDueSoon, cancellationToken);
            await notificationWriter.RemoveBySourceEntityAsync(companyId, taskId, NotificationType.TaskOverdue, cancellationToken);
        }
    }
}
