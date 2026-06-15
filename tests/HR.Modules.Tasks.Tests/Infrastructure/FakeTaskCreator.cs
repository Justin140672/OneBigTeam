using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeTaskCreator : ITaskCreator
{
    public record CreatedTask(
        Guid CompanyId, Guid CreatedBy, string Title, string? Description,
        TaskPriority Priority, TaskSource Source, DateOnly? DueDate,
        Guid? AssignedEmployeeId, Guid? AssignedUserId);

    public List<CreatedTask> Created { get; } = [];

    public Task<Guid> CreateAsync(
        Guid companyId, Guid createdBy, string title, string? description,
        TaskPriority priority, TaskSource source, DateOnly? dueDate,
        Guid? assignedEmployeeId, Guid? assignedUserId,
        CancellationToken cancellationToken)
    {
        Created.Add(new CreatedTask(
            companyId, createdBy, title, description,
            priority, source, dueDate, assignedEmployeeId, assignedUserId));

        return Task.FromResult(Guid.NewGuid());
    }
}
