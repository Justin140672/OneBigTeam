using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Sickness.Tests.Infrastructure;

/// <summary>
/// Captures <see cref="ITaskRescheduler"/> calls made by FitNoteEvidenceRequestService when a
/// closed absence's authoritative end date re-anchors an existing fit-note evidence request.
/// </summary>
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
