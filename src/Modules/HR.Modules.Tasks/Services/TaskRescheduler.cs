using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

// OFF-02: bulk reschedule-by-source, sibling of TaskCanceller.CancelManyBySourceEntitiesAsync.
internal sealed class TaskRescheduler(
    TasksDbContext dbContext, INotificationWriter notificationWriter, IClock clock) : ITaskRescheduler
{
    public async Task<int> RescheduleManyBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        DateOnly newDueDate,
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

        // Reschedule returns false (and leaves UpdatedAt untouched) when newDueDate already
        // matches — this is what makes repeated calls with the same date a genuine no-op rather
        // than just skipping the write: no stale notifications get cleared and no "date changed"
        // notification gets sent a second time for an unchanged date.
        var changedTasks = tasks.Where(t => t.Reschedule(newDueDate, now)).ToList();

        if (changedTasks.Count == 0)
            return 0;

        await dbContext.SaveChangesAsync(cancellationToken);

        await RemovePendingNotificationsAsync(companyId, changedTasks.Select(t => t.Id), cancellationToken);
        await NotifyAssigneesAsync(changedTasks, newDueDate, now, cancellationToken);

        return changedTasks.Count;
    }

    // A task's due date moving in either direction can make an already-sent TaskDueSoon/
    // TaskOverdue notification stale (e.g. moving later means an "overdue" notice no longer
    // applies; moving earlier means a "due soon" notice understates the urgency). Clearing both
    // lets DueSoonNotifier's next hourly pass recompute accurately from the new date rather than
    // leaving a wrong notification sitting in someone's inbox.
    private async Task RemovePendingNotificationsAsync(
        Guid companyId, IEnumerable<Guid> taskIds, CancellationToken cancellationToken)
    {
        foreach (var taskId in taskIds)
        {
            await notificationWriter.RemoveBySourceEntityAsync(companyId, taskId, NotificationType.TaskDueSoon, cancellationToken);
            await notificationWriter.RemoveBySourceEntityAsync(companyId, taskId, NotificationType.TaskOverdue, cancellationToken);
        }
    }

    // One notification per assignee per reschedule call, regardless of how many of their tasks
    // moved — dedupe by AssignedEmployeeId rather than firing once per task.
    private async Task NotifyAssigneesAsync(
        IReadOnlyCollection<TaskItem> changedTasks,
        DateOnly newDueDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var assigneeIds = changedTasks
            .Where(t => t.AssignedEmployeeId.HasValue)
            .Select(t => t.AssignedEmployeeId!.Value)
            .Distinct();

        foreach (var assigneeId in assigneeIds)
        {
            var companyId = changedTasks.First(t => t.AssignedEmployeeId == assigneeId).CompanyId;

            await notificationWriter.WriteAsync(
                Guid.NewGuid(), companyId, assigneeId,
                "A task's due date has changed",
                $"One or more of your tasks now has a new due date of {newDueDate:d MMM yyyy}.",
                sourceEntityId: assigneeId,
                NotificationType.TaskDateChanged,
                NotificationPriority.Normal,
                now,
                cancellationToken);
        }
    }
}
