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
}
