using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

/// <summary>
/// Configurable fake keyed by (sourceEntityId, actionType) so tests can prove the actionType
/// filter genuinely narrows the match — an entry registered for one actionType must not be
/// returned when the reader is queried with a different (or no) actionType filter, mirroring
/// the real OpenTaskBySourceEntityReader's SQL-level filter.
/// </summary>
internal sealed class FakeOpenTaskBySourceEntityReader : IOpenTaskBySourceEntityReader
{
    private readonly List<(Guid SourceEntityId, TaskActionType ActionType, Guid TaskId)> _openTasks = [];

    public void AddOpenTask(Guid sourceEntityId, TaskActionType actionType, Guid? taskId = null) =>
        _openTasks.Add((sourceEntityId, actionType, taskId ?? Guid.NewGuid()));

    public Task<IReadOnlyDictionary<Guid, Guid>> GetOpenTaskIdsAsync(
        Guid companyId,
        IEnumerable<Guid> sourceEntityIds,
        CancellationToken cancellationToken,
        TaskActionType? actionType = null)
    {
        var ids = sourceEntityIds.ToHashSet();

        var matches = _openTasks
            .Where(t => ids.Contains(t.SourceEntityId))
            .Where(t => actionType == null || t.ActionType == actionType.Value)
            // Mirror "most relevant single open task per source entity" — first match wins.
            .GroupBy(t => t.SourceEntityId)
            .ToDictionary(g => g.Key, g => g.First().TaskId);

        IReadOnlyDictionary<Guid, Guid> result = matches;
        return Task.FromResult(result);
    }

    public void AddOpenTaskForAssignee(Guid sourceEntityId, Guid assignedEmployeeId, TaskActionType actionType, Guid? taskId = null) =>
        _openTasksByAssignee.Add((sourceEntityId, assignedEmployeeId, actionType, taskId ?? Guid.NewGuid()));

    private readonly List<(Guid SourceEntityId, Guid AssignedEmployeeId, TaskActionType ActionType, Guid TaskId)> _openTasksByAssignee = [];

    public Task<Guid?> GetOpenTaskIdForAssigneeAsync(
        Guid companyId,
        Guid sourceEntityId,
        Guid assignedEmployeeId,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        var match = _openTasksByAssignee
            .Where(t => t.SourceEntityId == sourceEntityId && t.AssignedEmployeeId == assignedEmployeeId && t.ActionType == actionType)
            .Select(t => (Guid?)t.TaskId)
            .FirstOrDefault();

        return Task.FromResult(match);
    }
}
