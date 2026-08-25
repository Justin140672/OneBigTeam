using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HR.Modules.Offboarding;

namespace HR.Modules.Offboarding.Services;

// Wraps the existing StartOffboardingHandler so other modules (Employees, via the
// IOffboardingPlanCoordinator port declared in HR.Infrastructure.Abstractions) can trigger the
// same plan/checklist generation used by the manual "Start Offboarding" action, without
// duplicating its task-generation logic and without a direct module-to-module reference.
internal sealed class OffboardingPlanCoordinator(
    StartOffboardingHandler startOffboardingHandler,
    OffboardingDbContext dbContext,
    ITaskCanceller taskCanceller,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ILogger<OffboardingPlanCoordinator> logger) : IOffboardingPlanCoordinator
{
    public async Task StartAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly lastWorkingDay,
        string? notes,
        CancellationToken cancellationToken)
    {
        var request = new StartOffboardingRequest(companyId, employeeId, lastWorkingDay, notes);
        var result = await startOffboardingHandler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            // Best-effort side effect: by the time a Leaving Process is started, an offboarding
            // plan should never already exist for the same employee — the product flow doesn't
            // allow offboarding to be started manually ahead of a leaving process. If the
            // conflict (or any other failure, e.g. employee lookup) happens anyway, we log and
            // swallow rather than fail "Start Leaving Process": the LeavingProcess entity and the
            // employee's status change have already been committed by the caller and remain the
            // source of truth, and HR can still start offboarding manually from the Offboarding
            // tab if this automatic trigger didn't run.
            logger.LogWarning(
                "Could not auto-start offboarding plan for employee {EmployeeId} in company {CompanyId}: {Error}",
                employeeId, companyId, result.Error.Message);
        }
    }

    // OFF-01: called both (a) synchronously and in-process by Employees' CancelLeavingProcess
    // handler, and (b) by Offboarding's own consumer of EmployeeLeavingProcessCancelledIntegrationEvent
    // (CancelOffboardingOnLeavingProcessCancelledHandler) — the second, event-driven path exists
    // so that offboarding cancellation is not solely dependent on the direct in-process call
    // succeeding within the same request; IntegrationEventPublisher isolates each handler in its
    // own try/catch, so a failure here never aborts the Leaving Process cancellation itself.
    // Both callers converge on this one idempotent method, and it also doubles as the
    // reconciliation entry point (see OffboardingCancellationReconciliationJob): every code path
    // that could plausibly need to (re)synchronise a cancelled plan's tasks calls this same
    // method rather than duplicating the logic.
    //
    // Idempotent and safe to call any number of times for the same employee:
    //  - No plan at all, or the most recent plan already Completed: no-op (nothing to cancel;
    //    completed plans are a terminal state and must never be touched by this).
    //  - Plan not yet Cancelled: transitions it (and any outstanding local OffboardingTasks) to
    //    Cancelled exactly once, and publishes the audit event exactly once.
    //  - Plan already Cancelled (redelivered event, retried reconciliation, or the second of the
    //    two callers above running after the first already succeeded): the local
    //    plan/OffboardingTask transition is skipped (no duplicate audit event), but the
    //    cross-module Tasks-module sync below still always runs — this is what makes the method
    //    self-healing after a partial failure (e.g. the local transition committed but the
    //    Tasks-module call below failed or the process crashed before reaching it): a later call
    //    with the same arguments still finds the OffboardingTask ids and retries cancelling their
    //    Tasks-module TaskItems, which ITaskCanceller.CancelManyBySourceEntitiesAsync itself
    //    already treats as a no-op for anything already Completed/Cancelled.
    // Best-effort by design in every case: the caller's own transaction (leaving-process
    // cancellation, or the reconciliation job's own loop) must not fail because of anything that
    // happens here.
    public async Task CancelOutstandingTasksAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.OffboardingPlans
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            logger.LogInformation(
                "No offboarding plan found to cancel for employee {EmployeeId} in company {CompanyId}.",
                employeeId, companyId);
            return;
        }

        if (plan.Status == OffboardingStatus.Completed)
        {
            logger.LogInformation(
                "Offboarding plan {OffboardingPlanId} for employee {EmployeeId} is already Completed — " +
                "leaving process cancellation has no effect on a completed plan.",
                plan.Id, employeeId);
            return;
        }

        var now = clock.UtcNowOffset();
        var alreadyCancelled = plan.Status == OffboardingStatus.Cancelled;

        if (!alreadyCancelled)
        {
            var outstandingTasks = await dbContext.OffboardingTasks
                .Where(t => t.OffboardingPlanId == plan.Id
                    && t.Status != OffboardingTaskStatus.Completed
                    && t.Status != OffboardingTaskStatus.Skipped)
                .ToListAsync(cancellationToken);

            foreach (var task in outstandingTasks)
                task.Skip(now);

            plan.Cancel("Cancelled — employee's leaving process was withdrawn.", now);

            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEventPublisher.PublishAsync(
                new OffboardingPlanCancelledAuditEvent(
                    plan.CompanyId,
                    plan.Id,
                    plan.EmployeeId,
                    outstandingTasks.Count,
                    now),
                cancellationToken);
        }

        // Always re-run the cross-module sync, regardless of whether the plan was just
        // transitioned above or was already Cancelled — see the idempotency note above. The full
        // set of the plan's OffboardingTask ids is passed every time; ITaskCanceller filters out
        // anything already Completed/Cancelled on its side, so a repeat call with the same ids is
        // cheap and a genuine no-op once everything is in sync.
        var allOffboardingTaskIds = await dbContext.OffboardingTasks
            .Where(t => t.OffboardingPlanId == plan.Id)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (allOffboardingTaskIds.Count == 0)
            return;

        var tasksModuleCancelledCount = await taskCanceller.CancelManyBySourceEntitiesAsync(
            companyId, allOffboardingTaskIds, TaskSource.Offboarding, TaskActionType.Complete, cancellationToken);

        if (tasksModuleCancelledCount > 0)
        {
            logger.LogInformation(
                "Cancelled {Count} outstanding Tasks-module task(s) for offboarding plan {OffboardingPlanId} " +
                "(employee {EmployeeId}, company {CompanyId}).",
                tasksModuleCancelledCount, plan.Id, employeeId, companyId);
        }
    }
}
