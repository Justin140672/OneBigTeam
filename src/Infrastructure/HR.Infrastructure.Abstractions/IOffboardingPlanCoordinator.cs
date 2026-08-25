namespace HR.Infrastructure.Abstractions;

// Port used by Employees' StartLeavingProcess handler to auto-trigger the same
// plan/checklist generation as the manual "Start Offboarding" action, using the Leaving
// Process's Last Working Day, without a direct module-to-module reference. Implemented in
// HR.Modules.Offboarding by wrapping the existing StartOffboardingHandler.
public interface IOffboardingPlanCoordinator
{
    Task StartAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly lastWorkingDay,
        string? notes,
        CancellationToken cancellationToken);

    // Used by Employees' CancelLeavingProcess handler when a leaving process is withdrawn after
    // offboarding has already started. Cancels any outstanding (not yet completed/skipped)
    // OffboardingTasks and marks the OffboardingPlan itself as Cancelled, without a direct
    // module-to-module reference. Implemented in HR.Modules.Offboarding. A no-op (best-effort,
    // never throws) when no active plan exists for the employee.
    Task CancelOutstandingTasksAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken);

    // OFF-02: used by Offboarding's own consumer of EmployeeLeavingDateSetIntegrationEvent
    // (published by Employees' StartLeavingProcess and AmendLeavingProcess handlers whenever the
    // leaving date/last working day is set or amended) to keep the active offboarding plan's
    // LastWorkingDay, and every outstanding (not Completed/Skipped) OffboardingTask's due date,
    // aligned with the current last working day. Completed/Skipped tasks are never touched.
    // Idempotent and safe to call any number of times with the same newLastWorkingDay: no plan, or
    // the most recent plan already Completed/Cancelled, is a no-op; a plan/tasks already at
    // newLastWorkingDay are left untouched (no duplicate audit event, no duplicate notification).
    // Best-effort by design: the caller's own transaction must not fail because of anything that
    // happens here.
    Task RescheduleOutstandingTasksAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly newLastWorkingDay,
        CancellationToken cancellationToken);
}
