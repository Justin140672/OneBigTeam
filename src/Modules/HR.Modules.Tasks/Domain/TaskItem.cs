namespace HR.Modules.Tasks.Domain;

internal sealed class TaskItem
{
    private TaskItem() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TaskItemStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public Guid? AssignedEmployeeId { get; private set; }
    public Guid? AssignedUserId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? CompletedBy { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TaskItem Create(
        Guid id,
        Guid companyId,
        Guid createdBy,
        string title,
        string? description,
        TaskPriority priority,
        DateOnly? dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId,
        DateTimeOffset now)
    {
        return new TaskItem
        {
            Id = id,
            CompanyId = companyId,
            CreatedBy = createdBy,
            Title = title,
            Description = description,
            Priority = priority,
            DueDate = dueDate,
            AssignedEmployeeId = assignedEmployeeId,
            AssignedUserId = assignedUserId,
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

    public void Complete(Guid completedBy, DateTimeOffset now)
    {
        if (Status == TaskItemStatus.Completed)
            return;

        if (Status == TaskItemStatus.Cancelled)
            throw new InvalidOperationException("Cannot complete a cancelled task.");

        Status = TaskItemStatus.Completed;
        CompletedBy = completedBy;
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

    public void Reassign(Guid? assignedEmployeeId, Guid? assignedUserId, DateTimeOffset now)
    {
        AssignedEmployeeId = assignedEmployeeId;
        AssignedUserId = assignedUserId;
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
