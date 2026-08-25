using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Offboarding.Jobs;

/// <summary>
/// OFF-01: pragmatic, idempotent reconciliation for the cross-module hand-off between an
/// OffboardingPlan being cancelled and its corresponding Tasks-module TaskItems actually being
/// cancelled. IntegrationEventPublisher already isolates each event handler so a failure in
/// <see cref="OffboardingPlanCoordinator.CancelOutstandingTasksAsync"/> can never abort the
/// caller, but that also means such a failure is otherwise silent — this job is the periodic
/// on-demand catch-up for that case, rather than any new distributed-transaction/outbox
/// infrastructure (none exists in this module and none is warranted for this).
///
/// Runs daily: finds every Cancelled OffboardingPlan and simply re-invokes the same idempotent
/// <see cref="OffboardingPlanCoordinator.CancelOutstandingTasksAsync"/> method the two normal
/// cancellation paths already use. For a plan that is already fully in sync, that call is a
/// cheap no-op (ITaskCanceller finds nothing left to cancel); for a plan where the Tasks-module
/// side previously failed to update, it retries and completes the cancellation.
/// </summary>
internal sealed class OffboardingCancellationReconciliationJob(
    OffboardingDbContext dbContext,
    IOffboardingPlanCoordinator offboardingPlanCoordinator,
    ILogger<OffboardingCancellationReconciliationJob> logger)
{
    public async Task ExecuteAsync()
    {
        var cancelledPlans = await dbContext.OffboardingPlans
            .AsNoTracking()
            .Where(p => p.Status == OffboardingStatus.Cancelled)
            .Select(p => new { p.CompanyId, p.EmployeeId })
            .Distinct()
            .ToListAsync();

        if (cancelledPlans.Count == 0)
            return;

        foreach (var plan in cancelledPlans)
        {
            try
            {
                await offboardingPlanCoordinator.CancelOutstandingTasksAsync(
                    plan.CompanyId, plan.EmployeeId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // One employee's reconciliation failing must never stop the rest of the batch
                // from being checked — same isolation principle IntegrationEventPublisher
                // already applies to individual event handlers.
                logger.LogError(
                    ex,
                    "Offboarding cancellation reconciliation failed for employee {EmployeeId} in company {CompanyId}.",
                    plan.EmployeeId, plan.CompanyId);
            }
        }
    }
}
