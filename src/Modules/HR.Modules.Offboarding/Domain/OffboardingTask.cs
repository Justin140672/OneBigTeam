namespace HR.Modules.Offboarding.Domain;

internal sealed class OffboardingTask
{
    private OffboardingTask() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid OffboardingPlanId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public OffboardingTaskAssignTo AssignTo { get; private set; }

    public Guid? AssignedEmployeeId { get; private set; }

    public Guid? AssetAssignmentId { get; private set; }

    public bool IsAssetReturnTask => AssetAssignmentId is not null;

    public bool RequiresHrConfirmation { get; private set; }

    public bool IsMandatory { get; private set; }

    public string? SkipReason { get; private set; }
    public Guid? SkippedByUserId { get; private set; }
    public DateTimeOffset? SkippedAt { get; private set; }

    public DateOnly? DueDate { get; private set; }
    public OffboardingTaskStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? TaskItemCreatedAt { get; private set; }

    public static OffboardingTask Create(
        Guid id,
        Guid companyId,
        Guid offboardingPlanId,
        string title,
        string? description,
        OffboardingTaskAssignTo assignTo,
        DateOnly? dueDate,
        DateTimeOffset now,
        Guid? assignedEmployeeId = null,
        Guid? assetAssignmentId = null,
        bool requiresHrConfirmation = false,
        bool isMandatory = true)
    {
        return new OffboardingTask
        {
            Id = id,
            CompanyId = companyId,
            OffboardingPlanId = offboardingPlanId,
            Title = title,
            Description = description,
            AssignTo = assignTo,
            AssignedEmployeeId = assignedEmployeeId,
            AssetAssignmentId = assetAssignmentId,
            RequiresHrConfirmation = requiresHrConfirmation,
            IsMandatory = isMandatory,
            DueDate = dueDate,
            Status = OffboardingTaskStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static OffboardingTask CreateWaived(
        Guid id,
        Guid companyId,
        Guid offboardingPlanId,
        string title,
        string description,
        OffboardingTaskAssignTo assignTo,
        DateOnly? dueDate,
        DateTimeOffset now)
    {
        var task = Create(
            id, companyId, offboardingPlanId, title, description, assignTo, dueDate, now,
            isMandatory: false);
        task.Waive(now, description, OffboardingSystemActor.Id);
        return task;
    }

    public void MarkTaskItemCreated(DateTimeOffset now)
    {
        TaskItemCreatedAt = now;
        UpdatedAt = now;
    }

    public bool ReassignTo(Guid employeeId, DateTimeOffset now)
    {
        if (AssignedEmployeeId == employeeId)
            return false;

        AssignedEmployeeId = employeeId;
        UpdatedAt = now;
        return true;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = OffboardingTaskStatus.Completed;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Skip(DateTimeOffset now, string reason, Guid actorUserId)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to skip an offboarding task.", nameof(reason));

        Status = OffboardingTaskStatus.Skipped;
        SkipReason = reason;
        SkippedByUserId = actorUserId;
        SkippedAt = now;
        UpdatedAt = now;
    }

    public void Waive(DateTimeOffset now, string reason, Guid actorUserId)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to waive an offboarding task.", nameof(reason));

        if (Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Waived
            or OffboardingTaskStatus.Cancelled)
            throw new InvalidOperationException($"Cannot waive an offboarding task with status '{Status}'.");

        Status = OffboardingTaskStatus.Waived;
        SkipReason = reason;
        SkippedByUserId = actorUserId;
        SkippedAt = now;
        UpdatedAt = now;
    }

    public void CancelBecauseLeavingProcessCancelled(DateTimeOffset now, Guid actorUserId)
    {
        if (Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Waived
            or OffboardingTaskStatus.Cancelled)
            return;

        Status = OffboardingTaskStatus.Cancelled;
        SkipReason = "Leaving process cancelled.";
        SkippedByUserId = actorUserId;
        SkippedAt = now;
        UpdatedAt = now;
    }

    public bool Reschedule(DateOnly newDueDate, DateTimeOffset now)
    {
        if (DueDate == newDueDate)
            return false;

        DueDate = newDueDate;
        UpdatedAt = now;
        return true;
    }
}
