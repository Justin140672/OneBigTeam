using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeOpenTaskBySourceEntityReader(
    Dictionary<Guid, Guid>? openTaskIds = null,
    Dictionary<Guid, Guid?>? taskAssignees = null) : IOpenTaskBySourceEntityReader
{
    private readonly IReadOnlyDictionary<Guid, Guid?> _taskAssignees =
        taskAssignees ?? new Dictionary<Guid, Guid?>();

    private readonly IReadOnlyDictionary<Guid, Guid> _openTaskIds =
        openTaskIds ?? new Dictionary<Guid, Guid>();

    public Task<IReadOnlyDictionary<Guid, Guid>> GetOpenTaskIdsAsync(
        Guid companyId,
        IEnumerable<Guid> sourceEntityIds,
        CancellationToken cancellationToken,
        TaskActionType? actionType = null) =>
        Task.FromResult(_openTaskIds);

    public Task<Guid?> GetOpenTaskIdForAssigneeAsync(
        Guid companyId,
        Guid sourceEntityId,
        Guid assignedEmployeeId,
        TaskActionType actionType,
        CancellationToken cancellationToken) =>
        Task.FromResult(_openTaskIds.TryGetValue(sourceEntityId, out var taskId) ? taskId : (Guid?)null);

    public Task<IReadOnlyDictionary<Guid, Guid?>> GetTaskAssigneesAsync(
        Guid companyId,
        IEnumerable<Guid> taskIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid?>>(
            taskIds.Where(_taskAssignees.ContainsKey).ToDictionary(id => id, id => _taskAssignees[id]));
}
