using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Leave.Tests.Infrastructure;

/// <summary>
/// Records every job "created" (i.e. enqueued) so Leave handler/job tests can assert what was
/// enqueued (e.g. LeavePolicyDeactivationJob) without a real Hangfire storage backend. Mirrors
/// HR.Modules.Identity.Tests.Infrastructure.RecordingBackgroundJobClient.
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
