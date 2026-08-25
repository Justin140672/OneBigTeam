using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

// OFF-06: bulk by-assignee reassignment, distinct from ITaskCanceller/ITaskRescheduler's
// source-entity-scoped bulk operations. Used when an employee departs and every task currently
// assigned to them (across every Source/ActionType) needs to move to a replacement, or be
// unassigned pending HR escalation.
internal sealed class TaskReassigner(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock) : ITaskReassigner
{
    public async Task<int> ReassignAllByAssigneeAsync(
        Guid companyId,
        Guid fromEmployeeId,
        Guid? toEmployeeId,
        CancellationToken cancellationToken)
    {
        var tasks = await dbContext.TaskItems
            .Where(t => t.CompanyId == companyId
                     && t.AssignedEmployeeId == fromEmployeeId
                     && t.Status != TaskItemStatus.Completed
                     && t.Status != TaskItemStatus.Cancelled)
            .ToListAsync(cancellationToken);

        if (tasks.Count == 0)
            return 0;

        var now = clock.UtcNowOffset();
        foreach (var task in tasks)
            task.Reassign(toEmployeeId, toEmployeeId, now);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (toEmployeeId is not null)
        {
            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                companyId,
                toEmployeeId.Value,
                "Tasks reassigned to you",
                $"{tasks.Count} task(s) previously assigned to a departed colleague have been reassigned to you.",
                fromEmployeeId,
                NotificationType.TaskAssigned,
                NotificationPriority.Normal,
                now,
                cancellationToken);
        }

        return tasks.Count;
    }
}
