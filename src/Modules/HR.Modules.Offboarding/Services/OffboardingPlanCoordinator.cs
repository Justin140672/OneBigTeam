using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Persistence;
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

    // Called by Employees' CancelLeavingProcess handler when HR withdraws a leaving process for
    // which offboarding has already started. Best-effort/no-op by design (mirrors StartAsync
    // above): the caller's own leaving-process cancellation and employee reactivation are the
    // source of truth and must not fail because of anything that happens here.
    public async Task CancelOutstandingTasksAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.OffboardingPlans
            .Where(p => p.CompanyId == companyId
                && p.EmployeeId == employeeId
                && p.Status != OffboardingStatus.Completed
                && p.Status != OffboardingStatus.Cancelled)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            logger.LogWarning(
                "No active offboarding plan found to cancel for employee {EmployeeId} in company {CompanyId}.",
                employeeId, companyId);
            return;
        }

        var outstandingTasks = await dbContext.OffboardingTasks
            .Where(t => t.OffboardingPlanId == plan.Id
                && t.Status != OffboardingTaskStatus.Completed
                && t.Status != OffboardingTaskStatus.Skipped)
            .ToListAsync(cancellationToken);

        var now = clock.UtcNowOffset();

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
}
