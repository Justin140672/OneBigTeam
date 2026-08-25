using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Offboarding.Tests.Infrastructure;

internal sealed class FakeTaskCreator : ITaskCreator
{
    public record CreatedTask(
        Guid CompanyId, Guid CreatedBy, string Title, string? Description,
        TaskPriority Priority, TaskSource Source, TaskActionType ActionType,
        DateOnly? DueDate, Guid? AssignedEmployeeId, Guid? AssignedUserId, Guid? SourceEntityId,
        bool NotifyAssignee = true);

    public List<CreatedTask> Created { get; } = [];

    // OFF-03: lets tests inject a cross-module failure for a specific task (by title or by its
    // OffboardingTask source id) to verify per-task isolation in OffboardingTaskSynchronizer /
    // reconciliation without needing a second parallel fake.
    public HashSet<string> TitlesToFail { get; } = [];
    public HashSet<Guid> SourceEntityIdsToFail { get; } = [];

    // Lets a test observe/assert arbitrary state (e.g. that the caller's DbContext already has no
    // pending Added entries) at the exact moment each CreateAsync call happens.
    public Action? OnCreateAsyncInvoked { get; set; }

    public Task<Guid> CreateAsync(
        Guid companyId, Guid createdBy, string title, string? description,
        TaskPriority priority, TaskSource source, TaskActionType actionType,
        DateOnly? dueDate, Guid? assignedEmployeeId, Guid? assignedUserId,
        Guid? sourceEntityId, CancellationToken cancellationToken,
        bool notifyAssignee = true)
    {
        OnCreateAsyncInvoked?.Invoke();

        if (TitlesToFail.Contains(title) || (sourceEntityId.HasValue && SourceEntityIdsToFail.Contains(sourceEntityId.Value)))
            throw new InvalidOperationException($"Simulated Tasks-module failure for '{title}'.");

        Created.Add(new CreatedTask(
            companyId, createdBy, title, description,
            priority, source, actionType, dueDate, assignedEmployeeId, assignedUserId, sourceEntityId,
            notifyAssignee));

        return Task.FromResult(Guid.NewGuid());
    }
}
