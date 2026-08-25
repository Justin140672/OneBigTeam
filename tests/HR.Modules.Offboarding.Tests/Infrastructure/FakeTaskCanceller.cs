using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeTaskCanceller : ITaskCanceller
{
    public record CancelledCall(Guid CompanyId, Guid SourceEntityId, TaskSource Source, TaskActionType ActionType);
    public record CancelManyCall(Guid CompanyId, IReadOnlyCollection<Guid> SourceEntityIds, TaskSource Source, TaskActionType ActionType);

    public List<CancelledCall> Calls { get; } = [];
    public List<CancelManyCall> CancelManyCalls { get; } = [];

    /// <summary>Number of tasks CancelManyBySourceEntitiesAsync should report as cancelled — configure per test.</summary>
    public int CancelManyReturnCount { get; set; }

    public Task CancelBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        Calls.Add(new CancelledCall(companyId, sourceEntityId, source, actionType));
        return Task.CompletedTask;
    }

    public Task<int> CancelAllBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        Calls.Add(new CancelledCall(companyId, sourceEntityId, source, actionType));
        return Task.FromResult(1);
    }

    public Task<int> CancelManyBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        CancelManyCalls.Add(new CancelManyCall(companyId, sourceEntityIds, source, actionType));
        return Task.FromResult(CancelManyReturnCount == 0 ? sourceEntityIds.Count : CancelManyReturnCount);
    }
}
