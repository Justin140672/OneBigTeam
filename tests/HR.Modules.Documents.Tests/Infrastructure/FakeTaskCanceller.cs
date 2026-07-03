using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeTaskCanceller : ITaskCanceller
{
    private readonly List<CancelledCall> _calls = [];

    public IReadOnlyList<CancelledCall> Calls => _calls;
    public int CallCount => _calls.Count;

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

    internal sealed record CancelledCall(
        Guid CompanyId,
        Guid SourceEntityId,
        TaskSource Source,
        TaskActionType ActionType);
}
