using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskLinkBySourceEntityReader(TasksDbContext dbContext) : ITaskLinkBySourceEntityReader
{
    public async Task<IReadOnlyDictionary<Guid, TaskLink>> GetTaskLinksAsync(
        Guid companyId,
        IEnumerable<Guid> sourceEntityIds,
        CancellationToken cancellationToken,
        TaskActionType? actionType = null)
    {
        var ids = sourceEntityIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, TaskLink>();

        var query = dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId != null
                     && ids.Contains(t.SourceEntityId.Value));

        if (actionType is not null)
            query = query.Where(t => t.ActionType == actionType.Value);

        var tasks = await query
            .Select(t => new
            {
                t.Id,
                SourceEntityId = t.SourceEntityId!.Value,
                t.AssignedEmployeeId,
                t.AssignedUserId,
                t.Status,
                t.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return tasks
            .GroupBy(t => t.SourceEntityId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var best = g
                        .OrderByDescending(t => t.Status == TaskItemStatus.Open || t.Status == TaskItemStatus.InProgress)
                        .ThenByDescending(t => t.CreatedAt)
                        .First();
                    return new TaskLink(
                        best.Id,
                        best.AssignedEmployeeId,
                        best.AssignedUserId,
                        best.Status == TaskItemStatus.Open || best.Status == TaskItemStatus.InProgress);
                });
    }
}
