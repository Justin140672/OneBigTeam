namespace HR.Infrastructure.Abstractions;

// OFF-03: read-only cross-module port implemented by HR.Modules.Employees, consumed by
// HR.Modules.Offboarding's OffboardingPlanCreationReconciliationJob to find every employee whose
// leaving process is InProgress but who currently has no active offboarding plan — the "missing
// plan" case the reconciliation job exists to detect and fix. No direct module-to-module reference:
// Offboarding depends only on this interface (declared in the shared Infrastructure.Abstractions
// project) and Employees provides the implementation.
public interface IActiveLeavingProcessReader
{
    /// <summary>
    /// Returns every employee, across every company, whose leaving process is currently InProgress.
    /// Unscoped by company because the reconciliation job runs once globally, mirroring
    /// OffboardingCancellationReconciliationJob's own unscoped query over OffboardingPlans.
    /// </summary>
    Task<IReadOnlyList<ActiveLeavingProcessItem>> GetInProgressLeavingProcessesAsync(
        CancellationToken cancellationToken);
}

public sealed record ActiveLeavingProcessItem(Guid CompanyId, Guid EmployeeId, DateOnly LastWorkingDay);
