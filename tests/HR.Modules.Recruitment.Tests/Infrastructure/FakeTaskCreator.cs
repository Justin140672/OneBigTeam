using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeTaskCreator : ITaskCreator
{
    private readonly List<CreatedTask> _created = [];
    private readonly Dictionary<string, Guid> _byKey = [];

    public IReadOnlyList<CreatedTask> Created => _created;

    public Func<int, Task>? AfterCreate { get; set; }

    public bool FailBeforePersist { get; set; }

    public Task<Guid> CreateAsync(
        Guid companyId,
        Guid createdBy,
        string title,
        string? description,
        TaskPriority priority,
        TaskSource source,
        TaskActionType actionType,
        DateOnly? dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId,
        Guid? sourceEntityId,
        CancellationToken cancellationToken,
        bool notifyAssignee = true,
        string? idempotencyKey = null)
    {
        if (FailBeforePersist)
            throw new InvalidOperationException("Tasks module unavailable.");

        if (idempotencyKey is not null && _byKey.TryGetValue(idempotencyKey, out var existing))
            return Task.FromResult(existing);

        var id = Guid.NewGuid();
        if (idempotencyKey is not null)
            _byKey[idempotencyKey] = id;

        _created.Add(new CreatedTask(
            id, companyId, title, description, priority, source, actionType,
            dueDate, assignedEmployeeId, assignedUserId, sourceEntityId, notifyAssignee));
        return AfterCreate is null ? Task.FromResult(id) : AfterCreateAsync(id);
    }

    private async Task<Guid> AfterCreateAsync(Guid id)
    {
        await AfterCreate!(_created.Count);
        return id;
    }

    internal sealed record CreatedTask(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        TaskPriority Priority,
        TaskSource Source,
        TaskActionType ActionType,
        DateOnly? DueDate,
        Guid? AssignedEmployeeId,
        Guid? AssignedUserId,
        Guid? SourceEntityId,
        bool NotifyAssignee = true);
}
