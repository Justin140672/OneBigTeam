using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeTaskCanceller : ITaskCanceller
{
    private readonly List<CancelledCall> _calls = [];

    public IReadOnlyList<CancelledCall> Calls => _calls;

    public bool Fail { get; set; }

    public Task CancelBySourceEntityAsync(
        Guid companyId, Guid sourceEntityId, TaskSource source, TaskActionType actionType, CancellationToken cancellationToken)
    {
        _calls.Add(new CancelledCall(companyId, [sourceEntityId], source, actionType));
        return Task.CompletedTask;
    }

    public Task<int> CancelAllBySourceEntityAsync(
        Guid companyId, Guid sourceEntityId, TaskSource source, TaskActionType actionType, CancellationToken cancellationToken)
    {
        _calls.Add(new CancelledCall(companyId, [sourceEntityId], source, actionType));
        return Task.FromResult(1);
    }

    public Task<int> CancelManyBySourceEntitiesAsync(
        Guid companyId, IReadOnlyCollection<Guid> sourceEntityIds, TaskSource source, TaskActionType actionType, CancellationToken cancellationToken)
    {
        if (Fail) throw new InvalidOperationException("Tasks module unavailable.");
        _calls.Add(new CancelledCall(companyId, sourceEntityIds.ToList(), source, actionType));
        return Task.FromResult(sourceEntityIds.Count);
    }

    internal sealed record CancelledCall(
        Guid CompanyId, IReadOnlyList<Guid> SourceEntityIds, TaskSource Source, TaskActionType ActionType);
}
