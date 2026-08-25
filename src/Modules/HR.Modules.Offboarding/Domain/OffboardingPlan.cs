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
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OffboardingPlan Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        DateOnly lastWorkingDay,
        string? notes,
        DateTimeOffset now)
    {
        return new OffboardingPlan
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LastWorkingDay = lastWorkingDay,
            Status = OffboardingStatus.NotStarted,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
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

    // OFF-02: shifts the plan's LastWorkingDay when HR amends the employee's leaving/last working
    // date. Returns false (and leaves everything untouched) when newLastWorkingDay already matches
    // — this is what makes IOffboardingPlanCoordinator.RescheduleOutstandingTasksAsync idempotent
    // for the plan itself: replaying the same amendment is a safe no-op.
    public bool Reschedule(DateOnly newLastWorkingDay, DateTimeOffset now)
    {
        if (LastWorkingDay == newLastWorkingDay)
            return false;

        LastWorkingDay = newLastWorkingDay;
        UpdatedAt = now;
        return true;
    }
}
