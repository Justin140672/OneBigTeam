using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeHrTaskCreator : IHrTaskCreator
{
    public record CreatedHrTask(
        Guid CompanyId, Guid CreatedBy, string Title, string? Description,
        TaskPriority Priority, TaskSource Source, TaskActionType ActionType,
        DateOnly? DueDate, Guid? SourceEntityId);

    public List<CreatedHrTask> Created { get; } = [];

    public Task<Guid> CreateForHrAsync(
        Guid companyId, Guid createdBy, string title, string? description,
        TaskPriority priority, TaskSource source, TaskActionType actionType,
        DateOnly? dueDate, Guid? sourceEntityId, CancellationToken cancellationToken)
    {
        Created.Add(new CreatedHrTask(
            companyId, createdBy, title, description, priority, source, actionType, dueDate, sourceEntityId));

        return Task.FromResult(Guid.NewGuid());
    }
}
