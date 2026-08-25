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

    // OFF-05: true when the plan's LastWorkingDay was already on or before "today" (the company's
    // local date) at the moment the plan was created — i.e. offboarding is starting retroactively
    // for someone whose departure has already happened rather than one being planned ahead of time.
    // Captured once at creation (never recomputed later) so it reflects the fact pattern that
    // actually drove the backdated-specific task generation below, regardless of how much later
    // "today" is compared to when this plan row is read back.
    public bool IsBackdated { get; private set; }

    // OFF-05: true whenever this plan currently has at least one outstanding OffboardingTask that
    // requires explicit HR confirmation (see OffboardingTask.RequiresHrConfirmation) — the asset/
    // document/access reconciliation work generated for a backdated departure. Persisted (rather
    // than computed on every read) so it can be surfaced/queried cheaply (e.g. an HR "needs
    // reconciliation" list) and so the alert stays visible even if nothing else about the plan
    // changes. Set true at creation by MarkHrReconciliationRequired when backdated task generation
    // produces at least one such task, and cleared by ResolveHrReconciliation once every
    // RequiresHrConfirmation task on the plan reaches a terminal state (Completed/Skipped).
    public bool RequiresHrReconciliation { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

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

    // OFF-05: raised once, at plan-creation time, when backdated task generation produced at least
    // one OffboardingTask requiring explicit HR confirmation. Idempotent by construction — it is
    // only ever called once per plan, from within StartOffboardingHandler before the plan is first
    // persisted, so there is no repeat-call/duplicate-alert concern here.
    public void MarkHrReconciliationRequired(DateTimeOffset now)
    {
        RequiresHrReconciliation = true;
        UpdatedAt = now;
    }

    // OFF-05: called once every outstanding RequiresHrConfirmation task on this plan has reached a
    // terminal state. Safe to call repeatedly — a plan that isn't currently flagged is left
    // untouched (no spurious UpdatedAt bump, no duplicate audit-worthy state change).
    public void ResolveHrReconciliation(DateTimeOffset now)
    {
        if (!RequiresHrReconciliation)
            return;

        RequiresHrReconciliation = false;
        UpdatedAt = now;
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
