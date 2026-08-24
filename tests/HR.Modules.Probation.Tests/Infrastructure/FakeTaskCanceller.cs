using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Probation.Tests.Infrastructure;

internal sealed class FakeTaskCanceller : ITaskCanceller
{
    public record CancelledCall(Guid CompanyId, Guid SourceEntityId, TaskSource Source, TaskActionType ActionType);

    public List<CancelledCall> Calls { get; } = [];

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
}
