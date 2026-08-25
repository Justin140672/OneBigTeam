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
    ITaskRescheduler taskRescheduler,
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
            // Best-effort side effect: the LeavingProcess entity and the employee's status change
            // have already been committed by the caller and remain the source of truth, so a
            // failure here must not fail "Start Leaving Process" itself. This is deliberately NOT
            // treated as a log-only, unrecoverable condition (OFF-03): a Conflict means a plan
            // already exists (nothing to do), but any other failure leaves this employee's
            // in-progress leaving process without an active offboarding plan — a state
            // OffboardingPlanCreationReconciliationJob actively detects (via
            // IActiveLeavingProcessReader) and repairs automatically on its next daily run, and HR
            // can also start offboarding manually from the Offboarding tab in the meantime.
            // Logged at Error (not Warning) for anything other than the expected Conflict case,
            // since it is a real gap until reconciliation closes it.
            var logLevel = result.Error.Code == "conflict" ? LogLevel.Information : LogLevel.Error;
            logger.Log(
                logLevel,
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

    // OFF-02: called by Offboarding's consumer of EmployeeLeavingDateSetIntegrationEvent whenever
    // Employees' StartLeavingProcess or AmendLeavingProcess handler sets/amends the leaving
    // date/last working day. Reconciles the active plan's LastWorkingDay and every outstanding
    // OffboardingTask's due date to the given newLastWorkingDay, then propagates the same date to
    // the corresponding Tasks-module TaskItems.
    //
    // Idempotent, mirroring CancelOutstandingTasksAsync's shape:
    //  - No plan at all, or the most recent plan already Completed/Cancelled: no-op — a leaving
    //    date can be amended before offboarding has started (nothing to reschedule yet; the plan
    //    will be created with the correct LastWorkingDay when it does start) or after it has
    //    finished/been withdrawn (a terminal plan must never be touched by this).
    //  - Plan's LastWorkingDay already equals newLastWorkingDay, and every outstanding task's
    //    DueDate already matches too: no local changes, no audit event — but the Tasks-module sync
    //    below still always runs (self-healing after a partial prior failure), same as cancellation.
    // Best-effort by design: this must never throw in a way that aborts the caller's own request.
    public async Task RescheduleOutstandingTasksAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly newLastWorkingDay,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.OffboardingPlans
            .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            logger.LogInformation(
                "No offboarding plan found to reschedule for employee {EmployeeId} in company {CompanyId}.",
                employeeId, companyId);
            return;
        }

        if (plan.Status is OffboardingStatus.Completed or OffboardingStatus.Cancelled)
        {
            logger.LogInformation(
                "Offboarding plan {OffboardingPlanId} for employee {EmployeeId} is already {Status} — " +
                "a leaving date amendment has no effect on it.",
                plan.Id, employeeId, plan.Status);
            return;
        }

        var now = clock.UtcNowOffset();
        var beforeLastWorkingDay = plan.LastWorkingDay;
        var planChanged = plan.Reschedule(newLastWorkingDay, now);

        var outstandingTasks = await dbContext.OffboardingTasks
            .Where(t => t.OffboardingPlanId == plan.Id
                && t.Status != OffboardingTaskStatus.Completed
                && t.Status != OffboardingTaskStatus.Skipped)
            .ToListAsync(cancellationToken);

        var rescheduledTaskCount = outstandingTasks.Count(t => t.Reschedule(newLastWorkingDay, now));

        if (planChanged || rescheduledTaskCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEventPublisher.PublishAsync(
                new OffboardingPlanRescheduledAuditEvent(
                    plan.CompanyId,
                    plan.Id,
                    plan.EmployeeId,
                    beforeLastWorkingDay,
                    newLastWorkingDay,
                    rescheduledTaskCount,
                    now),
                cancellationToken);
        }

        // Always re-run the cross-module sync, mirroring CancelOutstandingTasksAsync — this is
        // what makes the method self-healing if a previous call's local save succeeded but the
        // Tasks-module call below failed or the process crashed before reaching it. Passing every
        // outstanding OffboardingTask id every time is cheap: ITaskRescheduler only rewrites (and
        // notifies for) tasks whose TaskItem.DueDate doesn't already match newLastWorkingDay.
        if (outstandingTasks.Count == 0)
            return;

        var outstandingTaskIds = outstandingTasks.Select(t => t.Id).ToList();

        var tasksModuleRescheduledCount = await taskRescheduler.RescheduleManyBySourceEntitiesAsync(
            companyId, outstandingTaskIds, TaskSource.Offboarding, TaskActionType.Complete, newLastWorkingDay,
            cancellationToken);

        if (tasksModuleRescheduledCount > 0)
        {
            logger.LogInformation(
                "Rescheduled {Count} outstanding Tasks-module task(s) to {NewLastWorkingDay} for offboarding " +
                "plan {OffboardingPlanId} (employee {EmployeeId}, company {CompanyId}).",
                tasksModuleRescheduledCount, newLastWorkingDay, plan.Id, employeeId, companyId);
        }
    }
}
