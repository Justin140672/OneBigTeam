using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Integration.Tests.Infrastructure;

internal sealed class FakeBackgroundJobClient : IBackgroundJobClient
{
    private readonly List<Job> _createdJobs = [];
    private readonly object _lock = new();

    public IReadOnlyList<Job> CreatedJobs
    {
        get { lock (_lock) { return _createdJobs.ToList(); } }
    }

    public string Create(Job job, IState state)
    {
        lock (_lock) { _createdJobs.Add(job); }
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
