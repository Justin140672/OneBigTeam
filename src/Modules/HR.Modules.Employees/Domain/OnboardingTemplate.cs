using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Domain;

internal sealed class OnboardingTemplate
{
    private readonly List<OnboardingTemplateTask> _tasks = [];

    private OnboardingTemplate() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<OnboardingTemplateTask> Tasks => _tasks.AsReadOnly();

    public static OnboardingTemplate Create(
        Guid id,
        Guid companyId,
        string name,
        string? description,
        DateTimeOffset now)
    {
        return new OnboardingTemplate
        {
            Id = id,
            CompanyId = companyId,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string name, string? description, DateTimeOffset now)
    {
        Name = name;
        Description = description;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public OnboardingTemplateTask AddTask(
        Guid taskId,
        string title,
        string? description,
        TaskPriority priority,
        OnboardingTemplateTaskAssignTo assignTo,
        int dueDaysAfterStart,
        int displayOrder,
        DateTimeOffset now)
    {
        var task = OnboardingTemplateTask.Create(
            taskId,
            CompanyId,
            Id,
            title,
            description,
            priority,
            assignTo,
            dueDaysAfterStart,
            displayOrder);

        _tasks.Add(task);
        UpdatedAt = now;

        return task;
    }

    public void RemoveTask(Guid taskId, DateTimeOffset now)
    {
        var task = _tasks.SingleOrDefault(t => t.Id == taskId && t.IsActive);
        if (task is null)
            return;

        task.Deactivate();
        UpdatedAt = now;
    }

    /// <summary>
    /// Reconciles the active task checklist against a desired set of tasks in a single operation.
    /// Tasks with a matching existing <paramref name="desiredTasks"/> id are updated in place,
    /// tasks with no id are added as new, and any currently active task not present in
    /// <paramref name="desiredTasks"/> is deactivated. Used by the template edit screen, which
    /// replaces the whole checklist rather than exposing separate add/remove/reorder endpoints.
    /// </summary>
    public void ReplaceTasks(
        IReadOnlyList<(Guid? Id, string Title, string? Description, TaskPriority Priority, OnboardingTemplateTaskAssignTo AssignTo, int DueDaysAfterStart, int DisplayOrder)> desiredTasks,
        DateTimeOffset now)
    {
        var desiredIds = desiredTasks
            .Where(d => d.Id.HasValue)
            .Select(d => d.Id!.Value)
            .ToHashSet();

        foreach (var existingTask in _tasks.Where(t => t.IsActive && !desiredIds.Contains(t.Id)))
        {
            existingTask.Deactivate();
        }

        foreach (var desired in desiredTasks)
        {
            if (desired.Id.HasValue)
            {
                var existingTask = _tasks.SingleOrDefault(t => t.Id == desired.Id.Value && t.IsActive);
                if (existingTask is not null)
                {
                    existingTask.Update(
                        desired.Title,
                        desired.Description,
                        desired.Priority,
                        desired.AssignTo,
                        desired.DueDaysAfterStart,
                        desired.DisplayOrder);
                    continue;
                }
            }

            AddTask(
                Guid.NewGuid(),
                desired.Title,
                desired.Description,
                desired.Priority,
                desired.AssignTo,
                desired.DueDaysAfterStart,
                desired.DisplayOrder,
                now);
        }

        UpdatedAt = now;
    }
}
