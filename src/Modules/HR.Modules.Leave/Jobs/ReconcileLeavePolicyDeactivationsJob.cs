using Hangfire;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Daily sweep that re-enqueues any LeavePolicyDeactivationOnDeparture record still Pending or
/// Failed — covers the case where the initial Hangfire enqueue in
/// EmployeeDepartureFinalisedHandler itself never happened (process crashed between the durable
/// insert and the enqueue call) or where a Failed record needs a fresh round of retries after
/// investigation. Safe to run repeatedly: LeavePolicyDeactivationJob is itself idempotent
/// (Status == Processed short-circuits, Deactivate() is a no-op if already inactive).
/// </summary>
internal sealed class ReconcileLeavePolicyDeactivationsJob(
    LeaveDbContext dbContext,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ReconcileLeavePolicyDeactivationsJob> logger)
{
    public async Task ExecuteAsync()
    {
        var stuck = await dbContext.LeavePolicyDeactivationsOnDeparture
            .Where(d =>
                d.Status == LeavePolicyDeactivationOnDeparture.StatusPending ||
                d.Status == LeavePolicyDeactivationOnDeparture.StatusFailed)
            .ToListAsync();

        if (stuck.Count == 0)
            return;

        foreach (var request in stuck)
        {
            if (request.Status == LeavePolicyDeactivationOnDeparture.StatusFailed)
                request.ResetForRetry();

            backgroundJobClient.Enqueue<LeavePolicyDeactivationJob>(
                job => job.ProcessAsync(request.Id, request.CompanyId));
        }

        await dbContext.SaveChangesAsync();

        logger.LogInformation(
            "ReconcileLeavePolicyDeactivationsJob: re-enqueued {Count} stuck leave policy deactivation(s).",
            stuck.Count);
    }
}
