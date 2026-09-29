using Hangfire;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

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
