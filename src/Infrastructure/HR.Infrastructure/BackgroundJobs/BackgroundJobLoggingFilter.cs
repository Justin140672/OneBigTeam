using System.Diagnostics;
using Hangfire.Server;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.BackgroundJobs;

internal sealed class BackgroundJobLoggingFilter(ILogger<BackgroundJobLoggingFilter> logger) : IServerFilter
{
    private const string StopwatchKey = "hr_bg_stopwatch";

    public void OnPerforming(PerformingContext context)
    {
        context.Items[StopwatchKey] = Stopwatch.StartNew();

        logger.LogInformation(
            "Background job {BackgroundJobType}.{BackgroundJobMethod} (ID: {BackgroundJobId}) starting",
            context.BackgroundJob.Job.Type.Name,
            context.BackgroundJob.Job.Method.Name,
            context.BackgroundJob.Id);
    }

    public void OnPerformed(PerformedContext context)
    {
        long elapsed = 0;
        if (context.Items.TryGetValue(StopwatchKey, out var val) && val is Stopwatch sw)
        {
            sw.Stop();
            elapsed = sw.ElapsedMilliseconds;
        }

        var jobType = context.BackgroundJob.Job.Type.Name;
        var method = context.BackgroundJob.Job.Method.Name;
        var jobId = context.BackgroundJob.Id;

        if (context.Exception is not null && !context.ExceptionHandled)
        {
            logger.LogError(
                context.Exception,
                "Background job {BackgroundJobType}.{BackgroundJobMethod} (ID: {BackgroundJobId}) failed after {ElapsedMs}ms",
                jobType, method, jobId, elapsed);
        }
        else
        {
            logger.LogInformation(
                "Background job {BackgroundJobType}.{BackgroundJobMethod} (ID: {BackgroundJobId}) succeeded in {ElapsedMs}ms",
                jobType, method, jobId, elapsed);
        }
    }
}
