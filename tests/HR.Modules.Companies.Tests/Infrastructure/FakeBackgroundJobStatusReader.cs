using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="IBackgroundJobStatusReader"/> — returns a pre-configured
/// summary so GetCustomerSupportViewHandler tests can assert the mapped background-job fields
/// without a real Hangfire JobStorage.
/// </summary>
internal sealed class FakeBackgroundJobStatusReader : IBackgroundJobStatusReader
{
    public BackgroundJobStatusSummary SummaryToReturn { get; set; } =
        new(Available: true, ServerCount: 1, Enqueued: 0, Processing: 0, Scheduled: 0, Failed: 0, Succeeded: 0, Recurring: 0);

    public BackgroundJobStatusSummary GetStatus() => SummaryToReturn;

    public IReadOnlyList<BackgroundJobDetail> ScheduledJobsToReturn { get; set; } = [];
    public IReadOnlyList<BackgroundJobDetail> RunningJobsToReturn { get; set; } = [];
    public IReadOnlyList<BackgroundJobDetail> FailedJobsToReturn { get; set; } = [];
    public BackgroundJobRetryResult RetryResultToReturn { get; set; } = new(true, null);
    public string? LastRetriedJobId { get; private set; }

    public IReadOnlyList<BackgroundJobDetail> GetScheduledJobs(int count = 50) => ScheduledJobsToReturn;
    public IReadOnlyList<BackgroundJobDetail> GetRunningJobs(int count = 50) => RunningJobsToReturn;
    public IReadOnlyList<BackgroundJobDetail> GetFailedJobs(int count = 50) => FailedJobsToReturn;

    public BackgroundJobRetryResult RetryJob(string jobId)
    {
        LastRetriedJobId = jobId;
        return RetryResultToReturn;
    }
}
