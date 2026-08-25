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

    // OFF-03: snapshot of the employee/manager this task's corresponding Tasks-module TaskItem
    // should be assigned to, captured at generation time. Persisted (rather than recomputed) so the
    // durable-write-then-sync flow — and any later reconciliation retry — can create/recreate the
    // TaskItem without needing to re-resolve the manager hierarchy or asset ownership again.
    public Guid? AssignedEmployeeId { get; private set; }

    // OFF-04: links this task to the specific Assets-module AssetAssignment it represents, rather
    // than only carrying a free-text label (e.g. "Return asset: MacBook Pro"). Null for every
    // non-asset-return checklist item (document review, manager exit checklist, etc). Completing a
    // task with this set routes through IAssetReturnService instead of the generic
    // "mark complete" path — see CompleteOffboardingTaskFromTaskAction.
    public Guid? AssetAssignmentId { get; private set; }

    public bool IsAssetReturnTask => AssetAssignmentId is not null;

    // OFF-05: true for tasks generated as part of backdated-departure reconciliation — outstanding
    // assets, documents or access that HR must explicitly confirm/action because the employee who
    // would normally have handled them has already left (and may already have lost system access).
    // Distinct from AssignTo == HR on its own: several ordinary checklist items (e.g. the document
    // review task) are already HR-assigned regardless of backdating, but only reconciliation tasks
    // drive OffboardingPlan.RequiresHrReconciliation.
    public bool RequiresHrConfirmation { get; private set; }

    public DateOnly? DueDate { get; private set; }
    public OffboardingTaskStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // OFF-03: null until the corresponding Tasks-module TaskItem has actually been created for this
    // OffboardingTask. This is what makes "every general task references an existing offboarding
    // task" true in practice — the OffboardingTask row is always durable (committed) before its
    // TaskItem is attempted — and it is the marker OffboardingTaskSynchronizer and
    // OffboardingPlanCreationReconciliationJob use to find and complete/retry any task whose
    // Tasks-module counterpart was not created (partial failure, crash, or the process never having
    // reached the sync step yet).
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
        bool requiresHrConfirmation = false)
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
            DueDate = dueDate,
            Status = OffboardingTaskStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    // OFF-05: used to auto-complete/waive a checklist item that becomes irrelevant specifically
    // because the departure is backdated (e.g. "revoke system access" when access has already been
    // disabled synchronously by EmployeeDepartureFinalizer). Distinct from the plain Skip() below —
    // this variant is only ever invoked immediately at generation time, before the task has a
    // Tasks-module TaskItem, so OffboardingTaskSynchronizer's "Status != Skipped" filter simply never
    // picks it up and no TaskItem/notification is ever created for it in the first place.
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
        var task = Create(id, companyId, offboardingPlanId, title, description, assignTo, dueDate, now);
        task.Skip(now);
        return task;
    }

    // OFF-03: stamped by OffboardingTaskSynchronizer immediately after the corresponding
    // Tasks-module TaskItem is successfully created. Idempotent from the caller's point of view —
    // once set, this task is excluded from future sync/reconciliation passes.
    public void MarkTaskItemCreated(DateTimeOffset now)
    {
        TaskItemCreatedAt = now;
        UpdatedAt = now;
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
