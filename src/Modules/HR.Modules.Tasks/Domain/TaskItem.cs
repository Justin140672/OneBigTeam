namespace HR.Modules.Tasks.Domain;

internal sealed class TaskItem
{
    private TaskItem() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? AssignedToEmployeeId { get; private set; }
    public Guid CreatedByEmployeeId { get; private set; }
    public TaskItemStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TaskItem Create(
        Guid id,
        Guid companyId,
        Guid createdByEmployeeId,
        string title,
        string? description,
        TaskPriority priority,
        DateOnly? dueDate,
        Guid? assignedToEmployeeId,
        DateTimeOffset now)
    {
        return new TaskItem
        {
            Id = id,
            CompanyId = companyId,
            CreatedByEmployeeId = createdByEmployeeId,
            Title = title,
            Description = description,
            Priority = priority,
            DueDate = dueDate,
            AssignedToEmployeeId = assignedToEmployeeId,
            Status = TaskItemStatus.Open,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Start(DateTimeOffset now)
    {
        if (Status != TaskItemStatus.Open)
            throw new InvalidOperationException($"Cannot start a task with status '{Status}'.");

        Status = TaskItemStatus.InProgress;
        UpdatedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status == TaskItemStatus.Completed)
            return;

        if (Status == TaskItemStatus.Cancelled)
            throw new InvalidOperationException("Cannot complete a cancelled task.");

        Status = TaskItemStatus.Completed;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == TaskItemStatus.Cancelled)
            return;

        if (Status == TaskItemStatus.Completed)
            throw new InvalidOperationException("Cannot cancel a completed task.");

        Status = TaskItemStatus.Cancelled;
        UpdatedAt = now;
    }

    public void Reassign(Guid? employeeId, DateTimeOffset now)
    {
        AssignedToEmployeeId = employeeId;
        UpdatedAt = now;
    }

    public void UpdateDetails(string title, string? description, TaskPriority priority, DateOnly? dueDate, DateTimeOffset now)
    {
        Title = title;
        Description = description;
        Priority = priority;
        DueDate = dueDate;
        UpdatedAt = now;
    }
}
