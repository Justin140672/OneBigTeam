using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeTaskReassigner : ITaskReassigner
{
    public record ReassignCall(Guid CompanyId, Guid FromEmployeeId, Guid? ToEmployeeId);

    public List<ReassignCall> Calls { get; } = [];

    public int ReassignReturnCount { get; set; }

    public Task<int> ReassignAllByAssigneeAsync(
        Guid companyId,
        Guid fromEmployeeId,
        Guid? toEmployeeId,
        CancellationToken cancellationToken)
    {
        Calls.Add(new ReassignCall(companyId, fromEmployeeId, toEmployeeId));
        return Task.FromResult(ReassignReturnCount);
    }

    public record SourceReassignCall(
        Guid CompanyId, IReadOnlyCollection<Guid> SourceEntityIds, TaskSource Source, TaskActionType ActionType,
        Guid ToEmployeeId);

    public List<SourceReassignCall> SourceCalls { get; } = [];

    public Task<int> ReassignBySourceEntitiesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> sourceEntityIds,
        TaskSource source,
        TaskActionType actionType,
        Guid toEmployeeId,
        CancellationToken cancellationToken)
    {
        SourceCalls.Add(new SourceReassignCall(companyId, sourceEntityIds, source, actionType, toEmployeeId));
        return Task.FromResult(sourceEntityIds.Count);
    }
}
