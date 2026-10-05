using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeTaskLinkBySourceEntityReader(Dictionary<Guid, TaskLink>? links = null) : ITaskLinkBySourceEntityReader
{
    public Task<IReadOnlyDictionary<Guid, TaskLink>> GetTaskLinksAsync(
        Guid companyId,
        IEnumerable<Guid> sourceEntityIds,
        CancellationToken cancellationToken,
        TaskActionType? actionType = null)
    {
        var requested = sourceEntityIds.ToHashSet();
        IReadOnlyDictionary<Guid, TaskLink> result = (links ?? [])
            .Where(kv => requested.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return Task.FromResult(result);
    }
}
