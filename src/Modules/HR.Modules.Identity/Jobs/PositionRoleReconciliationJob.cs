using Hangfire;
using HR.Modules.Identity.Services;

namespace HR.Modules.Identity.Jobs;

/// <summary>
/// Ticket 6 follow-up: recurring wrapper around <see cref="PositionRoleReconciliationService"/> — see
/// that class for the reconciliation logic. Runs every 15 minutes (see
/// IdentityModule.UseIdentityRecurringJobs), in addition to the identical one-off call already made
/// on every startup (IdentityModule.ReconcilePositionRoleAssignmentsAsync), so a missed or failed
/// position-role sync (crashed consumer, delayed/duplicate/out-of-order integration event delivery)
/// recovers on its own within a documented interval instead of requiring a process restart.
/// <see cref="DisableConcurrentExecutionAttribute"/> prevents an overrun run and the next scheduled
/// tick from reconciling the same backlog concurrently.
/// </summary>
internal sealed class PositionRoleReconciliationJob(PositionRoleReconciliationService reconciliationService)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        await reconciliationService.ReconcileAllCompaniesAsync(CancellationToken.None);
    }
}
