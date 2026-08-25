using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Notifications.Tests.Infrastructure;

/// <summary>
/// Records every job "created" (i.e. enqueued — Hangfire's Enqueue&lt;T&gt; extension method is a
/// thin wrapper over IBackgroundJobClient.Create(Job, IState)) so NotificationWriterTests can assert
/// on what NotificationWriter.WriteAsync enqueues, without needing a real Hangfire storage backend.
/// Unlike NoOpBackgroundJobClient (which discards the job entirely) and
/// HR.Integration.Tests' FakeBackgroundJobClient (same), this fake keeps a record for inspection.
/// </summary>
internal sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    public List<Job> CreatedJobs { get; } = [];

    public string Create(Job job, IState state)
    {
        CreatedJobs.Add(job);
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
