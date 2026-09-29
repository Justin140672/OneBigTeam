namespace HR.Infrastructure.Abstractions;

public interface IOffboardingPlanCoordinator
{
    Task StartAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly lastWorkingDay,
        string? notes,
        Guid? replacementManagerEmployeeId,
        CancellationToken cancellationToken);

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
