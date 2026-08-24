using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeTaskCompleter : ITaskCompleter
{
    private readonly List<CompletedCall> _calls = [];
    private readonly List<CompletedForEmployeeCall> _forEmployeeCalls = [];

    public IReadOnlyList<CompletedCall> Calls => _calls;
    public int CallCount => _calls.Count;

    public IReadOnlyList<CompletedForEmployeeCall> ForEmployeeCalls => _forEmployeeCalls;

    public Task CompleteBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid completedBy,
        CancellationToken cancellationToken)
    {
        _calls.Add(new CompletedCall(companyId, sourceEntityId, source, actionType, completedBy));
        return Task.CompletedTask;
    }

    public Task CompleteBySourceEntityForEmployeeAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid assignedEmployeeId,
        Guid completedBy,
        CancellationToken cancellationToken)
    {
        _forEmployeeCalls.Add(new CompletedForEmployeeCall(
            companyId, sourceEntityId, source, actionType, assignedEmployeeId, completedBy));
        return Task.CompletedTask;
    }

    internal sealed record CompletedCall(
        Guid CompanyId,
        Guid SourceEntityId,
        TaskSource Source,
        TaskActionType ActionType,
        Guid CompletedBy);

    internal sealed record CompletedForEmployeeCall(
        Guid CompanyId,
        Guid SourceEntityId,
        TaskSource Source,
        TaskActionType ActionType,
        Guid AssignedEmployeeId,
        Guid CompletedBy);
}
