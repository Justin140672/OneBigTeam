using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeTaskCanceller : ITaskCanceller
{
    private readonly List<CancelledCall> _calls = [];
    private readonly List<CancelAllCall> _cancelAllCalls = [];

    public IReadOnlyList<CancelledCall> Calls => _calls;
    public int CallCount => _calls.Count;

    public IReadOnlyList<CancelAllCall> CancelAllCalls => _cancelAllCalls;
    public int CancelAllCallCount => _cancelAllCalls.Count;

    /// <summary>Number of tasks CancelAllBySourceEntityAsync should report as cancelled — configure per test.</summary>
    public int CancelAllReturnCount { get; set; }

    public Task CancelBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        _calls.Add(new CancelledCall(companyId, sourceEntityId, source, actionType));
        return Task.CompletedTask;
    }

    public Task<int> CancelAllBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken)
    {
        _cancelAllCalls.Add(new CancelAllCall(companyId, sourceEntityId, source, actionType));
        return Task.FromResult(CancelAllReturnCount);
    }

    internal sealed record CancelledCall(
        Guid CompanyId,
        Guid SourceEntityId,
        TaskSource Source,
        TaskActionType ActionType);

    internal sealed record CancelAllCall(
        Guid CompanyId,
        Guid SourceEntityId,
        TaskSource Source,
        TaskActionType ActionType);
}
