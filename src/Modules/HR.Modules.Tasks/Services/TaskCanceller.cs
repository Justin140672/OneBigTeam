using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
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
}
