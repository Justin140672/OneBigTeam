using Hangfire;
using HR.Modules.Employees.Services;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Jobs;

internal sealed class ReconcileLeavingProcessPropagationsJob(
    LeavingProcessPropagationService propagationService,
    ILogger<ReconcileLeavingProcessPropagationsJob> logger)
{
    public static readonly TimeSpan FailureResetAfter = TimeSpan.FromHours(6);

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var enqueued = await propagationService.EnqueueMissingAsync(
            LeavingProcessPropagationService.DefaultBatchSize, CancellationToken.None);

        var reset = await propagationService.ResetStaleFailuresAsync(
            FailureResetAfter, LeavingProcessPropagationService.DefaultBatchSize, CancellationToken.None);

        if (enqueued > 0 || reset > 0)
        {
            logger.LogWarning(
                "ReconcileLeavingProcessPropagationsJob: enqueued {Enqueued} missing propagation(s) and re-armed {Reset} terminally failed propagation(s).",
                enqueued, reset);
        }
    }
}
