using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeTaskRescheduler : ITaskRescheduler
{
    public record RescheduleManyCall(
        Guid CompanyId, IReadOnlyCollection<Guid> SourceEntityIds, TaskSource Source, TaskActionType ActionType, DateOnly NewDueDate);

    public List<RescheduleManyCall> RescheduleManyCalls { get; } = [];

    /// <summary>Number of tasks RescheduleManyBySourceEntitiesAsync should report as rescheduled — configure per test.</summary>
    public int RescheduleManyReturnCount { get; set; } = -1;

    public Task<int> RescheduleManyBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        DateOnly newDueDate,
        CancellationToken cancellationToken)
    {
        RescheduleManyCalls.Add(new RescheduleManyCall(companyId, sourceEntityIds, source, actionType, newDueDate));
        return Task.FromResult(RescheduleManyReturnCount < 0 ? sourceEntityIds.Count : RescheduleManyReturnCount);
    }
}
