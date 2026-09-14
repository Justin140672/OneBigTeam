using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Records every job "created" (i.e. enqueued — Hangfire's Enqueue&lt;T&gt; extension method is a
/// thin wrapper over IBackgroundJobClient.Create(Job, IState)) so Identity handler/job tests can
/// assert what was enqueued (e.g. AccountDisablementJob) without a real Hangfire storage backend.
/// Mirrors HR.Modules.Notifications.Tests.Infrastructure.RecordingBackgroundJobClient /
/// HR.Modules.Documents.Tests.Infrastructure.SpyBackgroundJobClient.
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
