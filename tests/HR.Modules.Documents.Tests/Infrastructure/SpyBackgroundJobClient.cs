using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Documents.Tests.Infrastructure;

/// <summary>
/// Records every job Create call so upload-handler tests can assert that a scan job was actually
/// enqueued (job type + method name), without a real Hangfire storage backend.
/// </summary>
internal sealed class SpyBackgroundJobClient : IBackgroundJobClient
{
    public List<Job> CreatedJobs { get; } = [];

    public string Create(Job job, IState state)
    {
        CreatedJobs.Add(job);
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
