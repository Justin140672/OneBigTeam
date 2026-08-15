namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Reads the current Hangfire job storage statistics for display in support/admin tooling.
/// Implemented in HR.Infrastructure (which owns the Hangfire JobStorage registration) so that
/// modules never take a direct dependency on Hangfire types — same "module consumes an
/// Infrastructure-owned abstraction" shape as IDocumentStorageReader/IReportExporter.
///
/// Deliberately platform-wide, not company-scoped: no background job in this codebase is tagged
/// with a company_id today (jobs are recurring/system-wide, e.g. document-review scans, support
/// notification retries), so there is no way to filter "outstanding jobs for customer X". Callers
/// (e.g. the Customer Support View) must present this as platform-wide health, not a per-customer
/// breakdown, until jobs carry tenant metadata.
/// </summary>
public interface IBackgroundJobStatusReader
{
    BackgroundJobStatusSummary GetStatus();

    /// <summary>Jobs currently scheduled to enqueue in the future (e.g. delayed jobs).</summary>
    IReadOnlyList<BackgroundJobDetail> GetScheduledJobs(int count = 50);

    /// <summary>Jobs currently being processed by a Hangfire server.</summary>
    IReadOnlyList<BackgroundJobDetail> GetRunningJobs(int count = 50);

    /// <summary>Jobs whose most recent execution ended in the Failed state.</summary>
    IReadOnlyList<BackgroundJobDetail> GetFailedJobs(int count = 50);

    /// <summary>
    /// Requeues a failed job for immediate re-execution using Hangfire's own Failed -&gt; Enqueued
    /// state transition (the same mechanism the Hangfire dashboard's "Retry" button uses).
    /// </summary>
    BackgroundJobRetryResult RetryJob(string jobId);
}

public sealed record BackgroundJobStatusSummary(
    bool Available,
    int ServerCount,
    int Enqueued,
    int Processing,
    int Scheduled,
    int Failed,
    int Succeeded,
    int Recurring);

/// <summary>
/// Job-level detail for platform admin job monitoring (Background Jobs epic, Job Monitoring story).
/// </summary>
public sealed record BackgroundJobDetail(
    string JobId,
    string JobName,
    string State,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? LastExecutedAt,
    DateTimeOffset? NextExecutionAt,
    int RetryCount,
    string? FailureReason);

public sealed record BackgroundJobRetryResult(bool Success, string? Error);
