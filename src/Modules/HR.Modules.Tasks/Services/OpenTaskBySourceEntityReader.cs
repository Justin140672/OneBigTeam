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
        CancellationToken cancellationToken)
    {
        var ids = sourceEntityIds.Distinct().ToList();

        if (ids.Count == 0)
            return new Dictionary<Guid, Guid>();

        // A source entity should only ever have one open (Open/InProgress) task at a time in
        // practice, but grouping + taking the most recent keeps this safe even if that
        // invariant is ever violated, rather than throwing on ToDictionaryAsync duplicates.
        var openTasks = await dbContext.TaskItems
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId != null
                     && ids.Contains(t.SourceEntityId.Value)
                     && (t.Status == TaskItemStatus.Open || t.Status == TaskItemStatus.InProgress))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, SourceEntityId = t.SourceEntityId!.Value })
            .ToListAsync(cancellationToken);

        return openTasks
            .GroupBy(t => t.SourceEntityId)
            .ToDictionary(g => g.Key, g => g.First().Id);
    }
}
