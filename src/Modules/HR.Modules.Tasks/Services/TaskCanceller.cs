using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCanceller(TasksDbContext dbContext, IClock clock) : ITaskCanceller
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
        return tasks.Count;
    }
}
