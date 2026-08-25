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
    public DateOnly? DueDate { get; private set; }
    public OffboardingTaskStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OffboardingTask Create(
        Guid id,
        Guid companyId,
        Guid offboardingPlanId,
        string title,
        string? description,
        OffboardingTaskAssignTo assignTo,
        DateOnly? dueDate,
        DateTimeOffset now)
    {
        return new OffboardingTask
        {
            Id = id,
            CompanyId = companyId,
            OffboardingPlanId = offboardingPlanId,
            Title = title,
            Description = description,
            AssignTo = assignTo,
            DueDate = dueDate,
            Status = OffboardingTaskStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Complete(DateTimeOffset now)
    {
        Status = OffboardingTaskStatus.Completed;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Skip(DateTimeOffset now)
    {
        Status = OffboardingTaskStatus.Skipped;
        UpdatedAt = now;
    }

    // OFF-02: shifts this task's due date to track a plan-level LastWorkingDay amendment. Every
    // OffboardingTask is created with DueDate == the plan's LastWorkingDay at generation time (see
    // StartOffboardingHandler), so rescheduling the plan reschedules every outstanding task to the
    // same new date. Returns false when DueDate already matches, so callers can detect a no-op —
    // completed/skipped tasks are filtered out by the caller before this is ever invoked, which is
    // what guarantees their DueDate is never rewritten.
    public bool Reschedule(DateOnly newDueDate, DateTimeOffset now)
    {
        if (DueDate == newDueDate)
            return false;

        DueDate = newDueDate;
        UpdatedAt = now;
        return true;
    }
}
