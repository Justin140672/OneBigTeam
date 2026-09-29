using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Services;

internal sealed class OpenTaskBySourceEntityReader(TasksDbContext dbContext) : IOpenTaskBySourceEntityReader
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetOpenTaskIdsAsync(
        Guid companyId,
        IEnumerable<Guid> sourceEntityIds,
        CancellationToken cancellationToken,
        TaskActionType? actionType = null)
    {
        var ids = sourceEntityIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, Guid>();

        var query = dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId != null
                     && ids.Contains(t.SourceEntityId.Value)
                     && (t.Status == TaskItemStatus.Open || t.Status == TaskItemStatus.InProgress));

        if (actionType is not null)
            query = query.Where(t => t.ActionType == actionType.Value);

        var openTasks = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, SourceEntityId = t.SourceEntityId!.Value })
            .ToListAsync(cancellationToken);

        return openTasks
            .GroupBy(t => t.SourceEntityId)
            .ToDictionary(g => g.Key, g => g.First().Id);
    }

    public async Task<Guid?> GetOpenTaskIdForAssigneeAsync(
        Guid companyId,
        Guid sourceEntityId,
        Guid assignedEmployeeId,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        return await dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId == sourceEntityId
                     && t.AssignedEmployeeId == assignedEmployeeId
                     && t.ActionType == actionType
                     && (t.Status == TaskItemStatus.Open || t.Status == TaskItemStatus.InProgress))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid?>> GetTaskAssigneesAsync(
        Guid companyId,
        IEnumerable<Guid> taskIds,
        CancellationToken cancellationToken)
    {
        var ids = taskIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, Guid?>();

        var tasks = await dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && ids.Contains(t.Id))
            .Select(t => new { t.Id, t.AssignedEmployeeId, t.AssignedUserId })
            .ToListAsync(cancellationToken);

        return tasks.ToDictionary(t => t.Id, t => t.AssignedEmployeeId ?? t.AssignedUserId);
    }
}
