using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using HR.Infrastructure.Abstractions;

namespace HR.Infrastructure.BackgroundJobs;

internal sealed class HangfireJobStatusReader(JobStorage jobStorage, IBackgroundJobClient backgroundJobClient)
    : IBackgroundJobStatusReader
{
    public BackgroundJobStatusSummary GetStatus()
    {
        try
        {
            var api = jobStorage.GetMonitoringApi();
            var servers = api.Servers();
            var stats = api.GetStatistics();

            return new BackgroundJobStatusSummary(
                Available: true,
                ServerCount: servers.Count,
                Enqueued: (int)stats.Enqueued,
                Processing: (int)stats.Processing,
                Scheduled: (int)stats.Scheduled,
                Failed: (int)stats.Failed,
                Succeeded: (int)stats.Succeeded,
                Recurring: (int)stats.Recurring);
        }
        catch
        {
            // Same defensive shape as InfrastructureModule's /health/background-jobs endpoint —
            // storage being unreachable should degrade this panel, not break the whole support view.
            return new BackgroundJobStatusSummary(
                Available: false,
                ServerCount: 0, Enqueued: 0, Processing: 0, Scheduled: 0, Failed: 0, Succeeded: 0, Recurring: 0);
        }
    }

    public IReadOnlyList<BackgroundJobDetail> GetScheduledJobs(int count = 50)
    {
        try
        {
            var api = jobStorage.GetMonitoringApi();
            return api.ScheduledJobs(0, count)
                .Select(pair => new BackgroundJobDetail(
                    JobId: pair.Key,
                    JobName: DescribeJob(pair.Value.Job),
                    State: "Scheduled",
                    ScheduledAt: pair.Value.ScheduledAt,
                    LastExecutedAt: null,
                    NextExecutionAt: pair.Value.EnqueueAt,
                    RetryCount: GetRetryCount(pair.Key),
                    FailureReason: null))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<BackgroundJobDetail> GetRunningJobs(int count = 50)
    {
        try
        {
            var api = jobStorage.GetMonitoringApi();
            return api.ProcessingJobs(0, count)
                .Select(pair => new BackgroundJobDetail(
                    JobId: pair.Key,
                    JobName: DescribeJob(pair.Value.Job),
                    State: "Processing",
                    ScheduledAt: null,
                    LastExecutedAt: pair.Value.StartedAt,
                    NextExecutionAt: null,
                    RetryCount: GetRetryCount(pair.Key),
                    FailureReason: null))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<BackgroundJobDetail> GetFailedJobs(int count = 50)
    {
        try
        {
            var api = jobStorage.GetMonitoringApi();
            return api.FailedJobs(0, count)
                .Select(pair => new BackgroundJobDetail(
                    JobId: pair.Key,
                    JobName: DescribeJob(pair.Value.Job),
                    State: "Failed",
                    ScheduledAt: null,
                    LastExecutedAt: pair.Value.FailedAt,
                    NextExecutionAt: null,
                    RetryCount: GetRetryCount(pair.Key),
                    FailureReason: pair.Value.ExceptionMessage))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Requeues via the same Failed -&gt; Enqueued state transition Hangfire's own dashboard "Retry"
    /// button performs (Hangfire.BackgroundJobClientExtensions.Requeue). Safe to call on a job that
    /// is no longer Failed — Hangfire simply reports the state transition as unsuccessful.
    /// </summary>
    public BackgroundJobRetryResult RetryJob(string jobId)
    {
        try
        {
            var succeeded = backgroundJobClient.ChangeState(jobId, new EnqueuedState(), FailedState.StateName);
            return succeeded
                ? new BackgroundJobRetryResult(true, null)
                : new BackgroundJobRetryResult(false, "The job was not in a Failed state, or no longer exists.");
        }
        catch (Exception ex)
        {
            return new BackgroundJobRetryResult(false, ex.Message);
        }
    }

    private static string DescribeJob(Job? job)
    {
        if (job is null)
            return "(unknown job)";

        return $"{job.Type.Name}.{job.Method.Name}";
    }

    /// <summary>
    /// Hangfire's AutomaticRetryAttribute persists the attempt count as a job parameter named
    /// "RetryCount" — reading it directly avoids re-fetching each job's full state history.
    /// </summary>
    private int GetRetryCount(string jobId)
    {
        try
        {
            using var connection = jobStorage.GetConnection();
            var raw = connection.GetJobParameter(jobId, "RetryCount");
            return string.IsNullOrWhiteSpace(raw) ? 0 : SerializationHelper.Deserialize<int>(raw);
        }
        catch
        {
            return 0;
        }
    }
}
