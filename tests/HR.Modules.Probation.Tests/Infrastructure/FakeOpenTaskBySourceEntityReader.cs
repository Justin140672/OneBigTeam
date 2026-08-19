using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Probation.Tests.Infrastructure;

internal sealed class FakeOpenTaskBySourceEntityReader(Dictionary<Guid, Guid>? openTaskIds = null) : IOpenTaskBySourceEntityReader
{
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
}
