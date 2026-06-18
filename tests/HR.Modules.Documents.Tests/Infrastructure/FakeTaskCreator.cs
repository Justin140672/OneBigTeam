using HR.SharedKernel;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeTaskCreator : ITaskCreator
{
    private readonly List<CreatedTask> _created = [];

    public IReadOnlyList<CreatedTask> Created => _created;

    public Task<Guid> CreateAsync(
        Guid companyId,
        Guid createdBy,
        string title,
        string? description,
        TaskPriority priority,
        TaskSource source,
        DateOnly? dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId,
        Guid? sourceEntityId,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        _created.Add(new CreatedTask(
            id, companyId, title, description, priority, source,
            dueDate, assignedEmployeeId, assignedUserId, sourceEntityId));
        return Task.FromResult(id);
    }

    internal sealed record CreatedTask(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        TaskPriority Priority,
        TaskSource Source,
        DateOnly? DueDate,
        Guid? AssignedEmployeeId,
        Guid? AssignedUserId,
        Guid? SourceEntityId);
}
