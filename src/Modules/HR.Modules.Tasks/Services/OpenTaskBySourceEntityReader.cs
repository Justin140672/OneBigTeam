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

        // A source entity should only ever have one open (Open/InProgress) task of a given
        // action type at a time in practice, but grouping + taking the most recent keeps this
        // safe even if that invariant is ever violated, rather than throwing on
        // ToDictionaryAsync duplicates. Without the actionType filter, a source entity can
        // legitimately have several concurrent open tasks of different action types (e.g. a
        // Shared Company Document with many per-employee open Acknowledge tasks alongside a
        // single open Review task) — callers that care about one specific kind must supply
        // actionType or they will match the wrong task.
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
}
