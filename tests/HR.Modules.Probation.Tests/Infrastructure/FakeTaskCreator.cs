using HR.SharedKernel;

namespace HR.Modules.Probation.Tests.Infrastructure;

internal sealed class FakeTaskCreator : ITaskCreator
{
    public record CreatedTask(
        Guid CompanyId, Guid CreatedBy, string Title, string? Description,
        TaskPriority Priority, TaskSource Source, TaskActionType ActionType,
        DateOnly? DueDate, Guid? AssignedEmployeeId, Guid? AssignedUserId, Guid? SourceEntityId);

    public List<CreatedTask> Created { get; } = [];

    public Task<Guid> CreateAsync(
        Guid companyId, Guid createdBy, string title, string? description,
        TaskPriority priority, TaskSource source, TaskActionType actionType,
        DateOnly? dueDate, Guid? assignedEmployeeId, Guid? assignedUserId,
        Guid? sourceEntityId, CancellationToken cancellationToken)
    {
        Created.Add(new CreatedTask(
            companyId, createdBy, title, description,
            priority, source, actionType, dueDate, assignedEmployeeId, assignedUserId, sourceEntityId));

        return Task.FromResult(Guid.NewGuid());
    }
}
