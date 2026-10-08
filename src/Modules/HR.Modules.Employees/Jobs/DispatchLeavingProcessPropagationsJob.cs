using Hangfire;
using HR.Modules.Employees.Services;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Jobs;

internal sealed class DispatchLeavingProcessPropagationsJob(
    LeavingProcessPropagationService propagationService,
    ILogger<DispatchLeavingProcessPropagationsJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var attempted = await propagationService.DispatchDueAsync(
            LeavingProcessPropagationService.DefaultBatchSize, CancellationToken.None);

        if (attempted > 0)
            logger.LogInformation("DispatchLeavingProcessPropagationsJob: attempted {Count} propagation(s).", attempted);
    }
}
