using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// Records every job "created" (i.e. enqueued — Hangfire's Enqueue&lt;T&gt; extension method is a
/// thin wrapper over IBackgroundJobClient.Create(Job, IState)) so Recruitment handler tests can
/// assert what was enqueued (e.g. PurgeCandidateDocumentStorageJob) without a real Hangfire storage
/// backend. Mirrors HR.Modules.Identity.Tests.Infrastructure.RecordingBackgroundJobClient.
/// </summary>
internal sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    public List<Job> CreatedJobs { get; } = [];

    /// <summary>The state each job in <see cref="CreatedJobs"/> was created with (same index) — lets
    /// tests tell an immediate Enqueue (EnqueuedState) from a delayed Schedule (ScheduledState).</summary>
    public List<IState> CreatedStates { get; } = [];

    public string Create(Job job, IState state)
    {
        CreatedJobs.Add(job);
        CreatedStates.Add(state);
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
