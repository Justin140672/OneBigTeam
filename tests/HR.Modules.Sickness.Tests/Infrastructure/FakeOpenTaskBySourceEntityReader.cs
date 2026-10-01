using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeOpenTaskBySourceEntityReader(
    Dictionary<Guid, Guid>? openTaskIds = null,
    Dictionary<Guid, Guid?>? taskAssignees = null) : IOpenTaskBySourceEntityReader
{
    private readonly IReadOnlyDictionary<Guid, Guid?> _taskAssignees =
        taskAssignees ?? new Dictionary<Guid, Guid?>();

    private readonly IReadOnlyDictionary<Guid, Guid> _openTaskIds =
        openTaskIds ?? new Dictionary<Guid, Guid>();

    public Guid? LastCompanyId { get; private set; }
    public IReadOnlyCollection<Guid>? LastSourceEntityIds { get; private set; }
    public TaskActionType? LastActionType { get; private set; }

    public Task<IReadOnlyDictionary<Guid, Guid>> GetOpenTaskIdsAsync(
        Guid companyId,
        IEnumerable<Guid> sourceEntityIds,
        CancellationToken cancellationToken,
        TaskActionType? actionType = null)
    {
        LastCompanyId = companyId;
        LastSourceEntityIds = sourceEntityIds.ToList();
        LastActionType = actionType;
        return Task.FromResult(_openTaskIds);
    }

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
