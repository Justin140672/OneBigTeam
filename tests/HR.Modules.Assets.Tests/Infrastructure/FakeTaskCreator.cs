using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Assets.Tests.Infrastructure;

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
        TaskActionType actionType,
        DateOnly? dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId,
        Guid? sourceEntityId,
        CancellationToken cancellationToken,
        bool notifyAssignee = true)
    {
        var id = Guid.NewGuid();
        _created.Add(new CreatedTask(
            id, companyId, createdBy, title, description, priority, source, actionType,
            dueDate, assignedEmployeeId, assignedUserId, sourceEntityId, notifyAssignee));
        return Task.FromResult(id);
    }

    internal sealed record CreatedTask(
        Guid Id,
        Guid CompanyId,
        Guid CreatedBy,
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
