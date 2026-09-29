namespace HR.Modules.Offboarding.Domain;

internal sealed class OffboardingPlan
{
    private OffboardingPlan() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly LastWorkingDay { get; private set; }
    public OffboardingStatus Status { get; private set; }
    public string? Notes { get; private set; }

    public bool IsBackdated { get; private set; }

    public bool RequiresHrReconciliation { get; private set; }

    public bool HasIncompleteOffboardingAtDeparture { get; private set; }

    public DateTimeOffset? FinalReviewTaskCreatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static bool CanComplete(IReadOnlyCollection<OffboardingTask> tasks)
    {
        if (tasks.Count == 0)
            return false;

        return tasks.All(t => t.IsMandatory
            ? t.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Waived
                or OffboardingTaskStatus.Cancelled
            : t.Status is OffboardingTaskStatus.Completed or OffboardingTaskStatus.Skipped
                or OffboardingTaskStatus.Waived or OffboardingTaskStatus.Cancelled);
    }

    public static OffboardingPlan Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        DateOnly lastWorkingDay,
        string? notes,
        DateTimeOffset now,
        bool isBackdated = false)
    {
        return new OffboardingPlan
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LastWorkingDay = lastWorkingDay,
            Status = OffboardingStatus.NotStarted,
            Notes = notes,
            IsBackdated = isBackdated,
            RequiresHrReconciliation = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void MarkHrReconciliationRequired(DateTimeOffset now)
    {
        RequiresHrReconciliation = true;
        UpdatedAt = now;
    }

    public void ResolveHrReconciliation(DateTimeOffset now)
    {
        if (!RequiresHrReconciliation)
            return;

        RequiresHrReconciliation = false;
        UpdatedAt = now;
    }

    public void MarkIncompleteOffboardingAtDeparture(DateTimeOffset now)
    {
        if (HasIncompleteOffboardingAtDeparture)
            return;

        HasIncompleteOffboardingAtDeparture = true;
        UpdatedAt = now;
    }

    public void ResolveIncompleteOffboardingAtDeparture(DateTimeOffset now)
    {
        if (!HasIncompleteOffboardingAtDeparture)
            return;

        HasIncompleteOffboardingAtDeparture = false;
        UpdatedAt = now;
    }

    public bool TryClaimFinalReviewTaskCreation(DateTimeOffset now)
    {
        if (FinalReviewTaskCreatedAt is not null)
            return false;

        FinalReviewTaskCreatedAt = now;
        UpdatedAt = now;
        return true;
    }

    public void Start(DateTimeOffset now)
    {
        Status = OffboardingStatus.InProgress;
        UpdatedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = OffboardingStatus.Completed;
        UpdatedAt = now;
    }

    public void Cancel(string? notes, DateTimeOffset now)
    {
        Status = OffboardingStatus.Cancelled;
        Notes = notes;
        UpdatedAt = now;
    }

    public bool Reschedule(DateOnly newLastWorkingDay, DateTimeOffset now)
    {
        if (LastWorkingDay == newLastWorkingDay)
            return false;

        LastWorkingDay = newLastWorkingDay;
        UpdatedAt = now;
        return true;
    }
}
