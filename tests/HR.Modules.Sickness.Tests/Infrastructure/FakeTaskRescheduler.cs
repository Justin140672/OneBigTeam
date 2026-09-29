using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeTaskRescheduler : ITaskRescheduler
{
    public List<(Guid CompanyId, IReadOnlyCollection<Guid> SourceEntityIds, TaskSource Source, TaskActionType ActionType, DateOnly NewDueDate)> Calls { get; } = [];

    public Task<int> RescheduleManyBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        DateOnly newDueDate,
        CancellationToken cancellationToken)
    {
        Calls.Add((companyId, sourceEntityIds, source, actionType, newDueDate));
        return Task.FromResult(sourceEntityIds.Count);
    }
}
