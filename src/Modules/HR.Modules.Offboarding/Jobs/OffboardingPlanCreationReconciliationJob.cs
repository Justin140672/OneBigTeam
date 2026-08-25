using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Offboarding.Jobs;

/// <summary>
/// OFF-03: guarantees "starting a leaving process results in exactly one active offboarding plan"
/// stays true even when the automatic trigger (IOffboardingPlanCoordinator.StartAsync, called
/// synchronously from Employees' StartLeavingProcessHandler) failed outright — e.g. a transient DB
/// error, or the process crashing between the Leaving Process being committed and the offboarding
/// plan creation call completing. That failure is intentionally no longer just logged and dropped:
/// this job is the recovery path, running daily like its sibling
/// <see cref="OffboardingCancellationReconciliationJob"/>.
///
/// Two responsibilities, both idempotent and safe to run any number of times:
///
///  1. Missing plans — every employee with an InProgress leaving process (from Employees, via
///     <see cref="IActiveLeavingProcessReader"/>) that has no active (not Completed/Cancelled)
///     OffboardingPlan gets one created via the same StartOffboardingHandler the manual/automatic
///     paths use. The unique partial index on (company_id, employee_id) is the final backstop
///     against ever creating a second one for the same employee, even under a rare race with
///     another creation path running concurrently — a conflict there is treated as already-resolved
///     and skipped, not an error.
///
///  2. Partially-synced plans — any already-durable OffboardingPlan that still has OffboardingTasks
///     without a corresponding Tasks-module TaskItem (TaskItemCreatedAt is null) has its sync
///     retried via OffboardingTaskSynchronizer. This is what completes a plan left in the
///     "Offboarding data committed, Tasks-module items missing" partial state described in OFF-03,
///     and is also how a duplicated-task condition would surface for HR (logged, see below) if the
///     count of pending tasks for a plan is unexpectedly high across repeated runs.
/// </summary>
internal sealed class OffboardingPlanCreationReconciliationJob(
    OffboardingDbContext dbContext,
    IActiveLeavingProcessReader activeLeavingProcessReader,
    StartOffboardingHandler startOffboardingHandler,
    OffboardingTaskSynchronizer taskSynchronizer,
    ILogger<OffboardingPlanCreationReconciliationJob> logger)
{
    public async Task ExecuteAsync()
    {
        await CreateMissingPlansAsync();
        await SyncPartiallySyncedPlansAsync();
    }

    private async Task CreateMissingPlansAsync()
    {
        var inProgressLeavingProcesses = await activeLeavingProcessReader.GetInProgressLeavingProcessesAsync(
            CancellationToken.None);

        if (inProgressLeavingProcesses.Count == 0)
            return;

        var employeesWithActivePlans = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.Status != OffboardingStatus.Completed && p.Status != OffboardingStatus.Cancelled)
            .Select(p => new { p.CompanyId, p.EmployeeId })
            .ToListAsync();

        var activePlanKeys = employeesWithActivePlans
            .Select(p => (p.CompanyId, p.EmployeeId))
            .ToHashSet();

        // Safety-net diagnostic: the unique partial index should make this impossible, but if it
        // is ever observed, HR needs visibility rather than silent data drift.
        var duplicateActivePlanGroups = employeesWithActivePlans
            .GroupBy(p => (p.CompanyId, p.EmployeeId))
            .Where(g => g.Count() > 1);

        foreach (var duplicate in duplicateActivePlanGroups)
        {
            logger.LogError(
                "Detected {Count} active offboarding plans for employee {EmployeeId} in company " +
                "{CompanyId} — expected at most one. Investigate manually.",
                duplicate.Count(), duplicate.Key.EmployeeId, duplicate.Key.CompanyId);
        }

        var missingPlans = inProgressLeavingProcesses
            .Where(lp => !activePlanKeys.Contains((lp.CompanyId, lp.EmployeeId)))
            .ToList();

        if (missingPlans.Count == 0)
            return;

        foreach (var missing in missingPlans)
        {
            try
            {
                var request = new StartOffboardingRequest(
                    missing.CompanyId, missing.EmployeeId, missing.LastWorkingDay, Notes: null);

                var result = await startOffboardingHandler.HandleAsync(request, CancellationToken.None);

                if (result.IsFailure)
                {
                    // Conflict here means another path (a concurrent run of this job, or the
                    // synchronous trigger finishing just before us) already created the plan —
                    // that's success from this job's point of view, not a failure to report.
                    if (result.Error.Code != "conflict")
                    {
                        logger.LogError(
                            "Offboarding plan creation reconciliation failed for employee {EmployeeId} " +
                            "in company {CompanyId}: {Error}",
                            missing.EmployeeId, missing.CompanyId, result.Error.Message);
                    }

                    continue;
                }

                logger.LogInformation(
                    "Created missing offboarding plan for employee {EmployeeId} in company {CompanyId} " +
                    "via reconciliation.",
                    missing.EmployeeId, missing.CompanyId);
            }
            catch (Exception ex)
            {
                // One employee's reconciliation failing must never stop the rest of the batch from
                // being checked — same isolation principle as OffboardingCancellationReconciliationJob.
                logger.LogError(
                    ex,
                    "Offboarding plan creation reconciliation threw for employee {EmployeeId} in " +
                    "company {CompanyId}.",
                    missing.EmployeeId, missing.CompanyId);
            }
        }
    }

    private async Task SyncPartiallySyncedPlansAsync()
    {
        var pendingPlanKeys = await dbContext.OffboardingTasks
            .AsNoTracking()
            .Where(t => t.TaskItemCreatedAt == null
                && t.Status != OffboardingTaskStatus.Skipped
                && t.Status != OffboardingTaskStatus.Completed)
            .Select(t => new { t.CompanyId, t.OffboardingPlanId })
            .Distinct()
            .ToListAsync();

        if (pendingPlanKeys.Count == 0)
            return;

        foreach (var plan in pendingPlanKeys)
        {
            try
            {
                var syncedCount = await taskSynchronizer.SyncPlanAsync(
                    plan.CompanyId, plan.OffboardingPlanId, CancellationToken.None);

                if (syncedCount > 0)
                {
                    logger.LogInformation(
                        "Reconciliation synced {Count} outstanding Tasks-module task(s) for " +
                        "offboarding plan {OffboardingPlanId} (company {CompanyId}).",
                        syncedCount, plan.OffboardingPlanId, plan.CompanyId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Offboarding task sync reconciliation threw for plan {OffboardingPlanId} in " +
                    "company {CompanyId}.",
                    plan.OffboardingPlanId, plan.CompanyId);
            }
        }
    }
}
